using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.Core.References;

namespace LibreKO.Editors.Desktop;
public partial class EventRowEditor : VBoxContainer
{
    readonly EventsWorkspace workspace;
    public EventRowEditor(EventsWorkspace workspace)
    {
        this.workspace = workspace;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public void Configure(EventDraft draft, EventTableDescriptor schema, EventRowKey key)
    {
        var generation = workspace.ViewGeneration;
        var database = workspace.Session.Generation;
        bool Current() => IsInstanceValid(this) && IsInsideTree() && workspace.Active && ReferenceEquals(workspace.Draft, draft) && workspace.ViewGeneration == generation && workspace.Session.Generation == database;
        var values = draft.Rows.Single(r => r.Key == key).Values;
        foreach (var group in schema.Fields.GroupBy(f => f.Field.Group))
        {
            var section = Ui.Group(this, group.Key);
            foreach (var descriptor in group)
            {
                var field = descriptor.Field;
                var raw = values[field.Name]?.ToString() ?? "";
                var reference = EventRules.ReferenceFor(schema.Table, field.Name, values);
                var column = Ui.Column(section);
                var label = descriptor.Label;
                if (schema.Table == EventTable.GameEvents && FieldValueParser.ReadInt32(values["Type"]) == 1)
                    label = field.Name switch
                    {
                        "Exec1" => "Destination zone",
                        "Exec2" => "Destination X",
                        "Exec3" => "Destination Z",
                        _ => label
                    };
                if (schema.Table == EventTable.MapObjects && field.Name == "ControlNpcId")
                    label = "Control target / warp group";
                Ui.Label(column, label, 11, Ui.Muted);
                if (!field.Writable)
                {
                    var display = Ui.Label(column, raw == "" ? "—" : raw, 13, field.Group == "Server state" ? Ui.Muted : Ui.Accent);
                    if (reference != null && int.TryParse(raw, out var readId) && readId != 0)
                    {
                        workspace.Session.ReferencePresenter.Bind(display, () => new(reference, readId), Current,
                            named => { display.Text = ReferenceLabels.Display(named); display.TooltipText = display.Text + "\n" + named.Source; });
                    }

                    continue;
                }

                var error = Ui.Label(column, "", 11, LibreKO.UiTheme.Bad);
                error.Visible = false;
                void Invalid(string message)
                {
                    if (!Current())
                        return;
                    workspace.InvalidFields.Add(key + "." + field.Name);
                    error.Text = message;
                    error.Visible = true;
                }

                void Set(JsonNode? value)
                {
                    if (!Current())
                        return;
                    workspace.InvalidFields.Remove(key + "." + field.Name);
                    error.Visible = false;
                    workspace.SetValue(key, field.Name, value);
                }

                if (field.Kind == FieldKind.Boolean)
                {
                    var check = Ui.Add(column, new CheckButton { Name = "Field_" + field.Name, Text = label, ButtonPressed = raw.Equals("true", StringComparison.OrdinalIgnoreCase) });
                    check.Toggled += v => Set(JsonValue.Create(v));
                    continue;
                }

                if (field.Choices != null)
                {
                    var option = Ui.Add(column, new OptionButton { Name = "Field_" + field.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
                    var choices = new List<string?>();
                    if (descriptor.Nullable)
                    {
                        option.AddItem("Every day");
                        choices.Add(null);
                    }

                    foreach (var choice in field.Choices)
                    {
                        option.AddItem(choice.Key + " · " + choice.Value);
                        choices.Add(choice.Key);
                    }

                    var selected = values[field.Name] == null ? choices.IndexOf(null) : choices.IndexOf(raw);
                    if (selected < 0)
                    {
                        selected = choices.Count;
                        choices.Add(raw);
                        option.AddItem(raw + " · Unknown stored value");
                    }

                    option.Select(selected);
                    option.ItemSelected += i =>
                    {
                        var choice = choices[(int)i];
                        Set(choice == null ? null : FieldValueParser.Parse(field, choice));
                    };
                    continue;
                }

                var row = Ui.Row(column);
                var input = Ui.Input(row, field.Kind == FieldKind.Text ? "Text" : field.Kind == FieldKind.DateOnly ? "yyyy-MM-dd" : "Stored value", raw);
                input.Name = "Field_" + field.Name;
                void Apply()
                {
                    if (!Current())
                        return;
                    try
                    {
                        Set(descriptor.Nullable && input.Text == "" ? null : FieldValueParser.Parse(field, input.Text));
                    }
                    catch (Exception e)
                    {
                        Invalid(e.Message);
                    }
                }

                workspace.PendingInputs.Add(() =>
                {
                    if (IsInstanceValid(input) && input.IsInsideTree() && input.HasFocus())
                        Apply();
                });
                input.TextChanged += _ => Apply();
                input.TextSubmitted += _ => Apply();
                input.FocusExited += Apply;
                if (reference != null)
                {
                    var meaning = Ui.Label(column, raw + " · resolving…", 11, Ui.Accent);
                    meaning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    Task Resolve()
                    {
                        workspace.Session.ReferencePresenter.Bind(meaning,
                            () => int.TryParse(input.Text, out var id) ? new(reference, id) : null, Current,
                            named => { meaning.Text = ReferenceLabels.Display(named); meaning.TooltipText = meaning.Text + "\n" + named.Source; });
                        return Task.CompletedTask;
                    }
                    if (reference == "npcs")
                        Ui.Label(column, schema.Table == EventTable.CollectionRaceObjectives
                            ? "Matches either type by ID; NPC-name aliases also match. Display names use monster-first precedence."
                            : "Runtime prototype lookup uses monster-first precedence. Only the numeric ID is stored.", 10, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    input.TextChanged += text => { _ = Resolve(); };
                    Ui.Button(row, "Browse…", () => workspace.Session.Shell.ShowReferencePicker(reference, entry =>
                    {
                        if (!Current())
                            return;
                        input.Text = entry.Id.ToString();
                        Apply();
                        meaning.Text = entry.Id + " · " + entry.Label;
                    }, Current));
                    input.TextSubmitted += text => _ = Resolve();
                    input.FocusExited += () => _ = Resolve();
                    _ = Resolve();
                }
            }
        }
    }
}
