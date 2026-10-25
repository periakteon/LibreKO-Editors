using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.References;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.Desktop;
public partial class WorldRowEditor(WorldWorkspace workspace) : VBoxContainer
{
    readonly List<Action> refresh = [];
    public void RefreshReferences() { foreach (var action in refresh) action(); }
    public void Configure(WorldDraft draft, WorldTableDescriptor schema, WorldKey key)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill; var view = workspace.ViewGeneration; var database = workspace.Session.Generation;
        bool Current() => IsInstanceValid(this) && IsInsideTree() && workspace.Active && ReferenceEquals(draft, workspace.Draft) && workspace.ViewGeneration == view && workspace.Session.Generation == database;
        var values = draft.Rows.Single(r => r.Key == key).Values;
        foreach (var group in schema.Fields.GroupBy(f => f.Field.Group))
        {
            var section = Ui.Group(this, group.Key);
            foreach (var descriptor in group)
            {
                var field = descriptor.Field; var raw = values[field.Name]?.ToString() ?? "";
                var column = Ui.Column(section); Ui.Label(column, descriptor.Label, 11, Ui.Muted);
                if (!field.Writable)
                {
                    var text = field.Choices?.TryGetValue(raw, out var label) == true ? raw + " · " + label : raw == "" ? "—" : raw;
                    var display = Ui.Label(column, text, 13, Ui.Accent); display.Name = "Key_" + field.Name;
                    var reference = WorldRules.ReferenceFor(schema.Table, field.Name, values);
                    if (reference != null) workspace.Session.ReferencePresenter.Bind(display, () => reference, Current, named => { display.Text = ReferenceLabels.Display(named); display.TooltipText = display.Text + "\n" + named.Source; });
                    continue;
                }
                var error = Ui.Label(column, "", 11, LibreKO.UiTheme.Bad); error.Visible = false;
                void Set(JsonNode? value)
                {
                    if (!Current() || !workspace.Session.Operations.CanMutate) return;
                    workspace.InvalidFields.Remove(key + "." + field.Name); error.Visible = false; workspace.SetValue(key, field.Name, value);
                }
                if (field.Kind == FieldKind.Boolean)
                { var toggle = Ui.Add(column, new CheckButton { Name = "Field_" + field.Name, ButtonPressed = raw.Equals("true", StringComparison.OrdinalIgnoreCase), Text = descriptor.Label }); toggle.Toggled += value => Set(JsonValue.Create(value)); continue; }
                if (field.Choices != null)
                {
                    var option = Ui.Add(column, new OptionButton { Name = "Field_" + field.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false }); var choices = new List<string?>();
                    if (descriptor.Nullable) { option.AddItem("None"); choices.Add(null); }
                    foreach (var choice in field.Choices) { option.AddItem(choice.Key + " · " + choice.Value); choices.Add(choice.Key); }
                    var selected = values[field.Name] == null ? choices.IndexOf(null) : choices.IndexOf(raw);
                    if (selected < 0) { selected = choices.Count; option.AddItem(raw + " · Unknown stored value"); choices.Add(raw); }
                    option.Select(selected); option.ItemSelected += i => Set(choices[(int)i] is string value ? FieldValueParser.Parse(field, value) : null); continue;
                }
                var row = Ui.Row(column); var input = Ui.Input(row, field.Kind == FieldKind.Text ? "Text" : "Stored value", raw); input.Name = "Field_" + field.Name;
                void Apply()
                {
                    if (!Current() || !workspace.Session.Operations.CanMutate) return;
                    try { Set(descriptor.Nullable && input.Text == "" ? null : FieldValueParser.Parse(field, input.Text)); }
                    catch (Exception e) { workspace.InvalidFields.Add(key + "." + field.Name); error.Text = e.Message; error.Visible = true; }
                }
                workspace.PendingInputs.Add(() => { if (IsInstanceValid(input) && input.IsInsideTree() && input.HasFocus()) Apply(); });
                input.TextChanged += _ => Apply(); input.TextSubmitted += _ => Apply(); input.FocusExited += Apply;
                if(schema.Table==WorldTable.MakeItemGroups&&field.Name=="Items")
                {var pool=Ui.Add(column,new OrderedItemPoolView(workspace.Session,input.Text,text=>{input.Text=text;Apply();},Current));input.TextChanged+=text=>pool.SetRaw(text);}
                if (schema.Table == WorldTable.NpcItems && field.Name.StartsWith("Percent", StringComparison.Ordinal))
                {
                    var percent = Ui.Label(column, "", 11, Ui.Muted); void Percentage() => percent.Text = decimal.TryParse(input.Text, out var chance) ? $"{chance / 100m:0.##}% · stored chance / 10000" : "Raw chance / 10000";
                    input.TextChanged += _ => Percentage(); Percentage();
                }
                if (field.Reference != null || WorldRules.ReferenceFor(schema.Table, field.Name, values) != null || schema.Table == WorldTable.GameEvents && field.Name == "Exec1")
                {
                    var meaning = Ui.Label(column, "", 11, Ui.Accent); meaning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    TextureRect? icon = null; if (field.Reference == "items" || schema.Table == WorldTable.NpcItems && field.Name.StartsWith("Item", StringComparison.Ordinal))
                        icon = Ui.Add(row, new TextureRect { Name = "ItemIcon_" + field.Name, CustomMinimumSize = new(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
                    ReferenceIdentity? Reference() { var current = draft.Rows.FirstOrDefault(r => r.Key == key)?.Values; return current == null ? null : WorldRules.ReferenceFor(schema.Table, field.Name, current); }
                    NamedReference? shown = null;
                    void Resolve()
                    {
                        if (!Current()) return;
                        var reference = Reference(); meaning.Visible = reference != null;
                        if (icon != null) { icon.Texture = reference?.Kind == "items" ? LibreKO.ItemIcons.Get(reference.Id) ?? LibreKO.UiIcons.Get("system/bag") : LibreKO.UiIcons.Get("system/bag"); icon.TooltipText = reference == null ? "No target" : reference.Id + " · " + reference.Kind; }
                        workspace.Session.ReferencePresenter.Bind(meaning, Reference, Current, named => { shown = named; meaning.Text = ReferenceLabels.Display(named); meaning.TooltipText = meaning.Text + "\n" + named.Source; });
                    }
                    refresh.Add(Resolve); input.TextChanged += _ => Resolve();
                    Ui.Button(row, "…", () =>
                    {
                        var reference = Reference(); if (reference == null) return;
                        ReferencePicker.Show(workspace.Session.Shell, workspace.Session, new(reference.Kind, IsMonster: reference.IsMonster, Zone: reference.Zone), named => { if (!Current()) return; input.Text = named.Key.Id.ToString(); Apply(); Resolve(); }, Current);
                    }).TooltipText = "Find a named reference";
                    Ui.Button(column, "Open related record", () => { if (Current() && shown is { Target: not null } resolved && resolved.Key == Reference()) _ = workspace.Session.Shell.OpenTargetAsync(resolved.Target); });
                    Resolve();
                }
            }
        }
    }
}
