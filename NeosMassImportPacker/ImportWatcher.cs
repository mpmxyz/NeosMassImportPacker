using BaseX;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.LogiX.Data;
using FrooxEngine.LogiX.Operators;
using FrooxEngine.LogiX.ProgramFlow;
using FrooxEngine.LogiX.WorldModel;
using FrooxEngine.UIX;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace NeosMassImportPacker
{
    internal class ImportWatcher
    {
        private const int MIN_COUNT = 2;
        private static readonly TimeSpan MAX_GAP = TimeSpan.FromSeconds(5);

        private const string DYN_IMPULSE_TAG = "pack";
        private const string DYN_VAR_SPACE = "entry";
        private const string DYN_VAR_ENTRY_SLOT = "entry/slot";
        private const string DYN_VAR_ENTRY_ACTIVE = "entry/active";

        private Slot firstRoot = null;
        private Slot listRoot = null;
        private DateTime lastUpdate = DateTime.UtcNow;
        private ICollection<Slot> alreadyImported = new List<Slot>();


        internal void OnImport(Slot imported)
        {
            UniLog.Log($"onImport {imported.Name}");
            DateTime now = DateTime.UtcNow;
            TimeSpan gap = now - lastUpdate;
            lock (this)
            {
                lastUpdate = now;
                if (firstRoot == null || gap > MAX_GAP)
                {
                    Init(imported);
                }

                alreadyImported.Add(imported);

                if (alreadyImported.Count >= MIN_COUNT)
                {
                    if (listRoot == null)
                    {
                        SpawnUI();
                        alreadyImported.Do(AddImportToUI);
                    }
                    else
                    {
                        AddImportToUI(imported);
                    }
                }

            }
        }

        private void SpawnUI()
        {
            UniLog.Log($"spawn ui");
            var ui = firstRoot.Parent.AddSlot("Mass Import Packer");
            ui.Tag = "Developer";
            ui.AttachComponent<Grabbable>();
            ui.GlobalPosition = firstRoot.GlobalPosition + firstRoot.Backward * 0.1f;
            ui.GlobalRotation = firstRoot.GlobalRotation;

            var panel = ui.AttachComponent<NeosCanvasPanel>();
            panel.Canvas.Slot.Tag = "Developer";
            panel.Panel.Title = "Mass Import Packer";
            panel.Panel.AddCloseButton();
            panel.CanvasSize = new float2(250, 500);
            panel.CanvasScale = 0.25f / panel.CanvasSize.x;

            var builder = new UIBuilder(panel.Canvas);
            builder.VerticalLayout(spacing: 4f);

            builder.Style.FlexibleHeight = 1;
            builder.ScrollArea();

            builder.Style.ForceExpandHeight = false;
            listRoot = builder.VerticalLayout(spacing: 4f, childAlignment: Alignment.TopLeft).Slot;
            builder.FitContent(horizontal: SizeFit.Disabled, vertical: SizeFit.MinSize);
            builder.NestOut();
            //builder.NestOut();

            builder.Style.FlexibleHeight = -1;
            builder.Style.PreferredHeight = 24f;

            var parentSlotField = ui.AttachComponent<ReferenceField<Slot>>();
            SyncMemberEditorBuilder.Build(parentSlotField.Reference, "Parent", null, builder);

            var button = builder.Button();
            var hasNoParent = button.Slot.AttachComponent<ReferenceEqualityDriver<Slot>>();
            var buttonTextDriver = button.Slot.AttachComponent<BooleanValueDriver<string>>();
            hasNoParent.TargetReference.Target = parentSlotField.Reference;
            hasNoParent.Target.Target = buttonTextDriver.State;
            buttonTextDriver.TargetField.Target = button.LabelTextField;
            buttonTextDriver.TrueValue.Value = "Pack into new Slot";
            buttonTextDriver.FalseValue.Value = "Reparent";

            builder.NestOut();

            var template = ui.AddSlot("Templates").AddSlot("Import Package");

            var logix = ui.AddSlot("LogiX");
            CreateLogiX(logix, listRoot, template, parentSlotField.Reference);

            var trigger = button.Slot.AttachComponent<ButtonDynamicImpulseTrigger>();
            trigger.Target.Target = logix;
            trigger.PressedTag.Value = DYN_IMPULSE_TAG;
        }

        private static void CreateLogiX(Slot root, Slot list, Slot template, SyncRef<Slot> parent)
        {
            var listRef = root.AttachComponent<ReferenceRegister<Slot>>();
            var templateRef = root.AttachComponent<ReferenceRegister<Slot>>();
            var parentRef = root.AttachComponent<ReferenceRegister<Slot>>();
            var parentRefDriver = parentRef.Slot.AttachComponent<ReferenceCopy<Slot>>();
            parentRefDriver.Target.Target = parentRef.Target;
            parentRefDriver.Source.Target = parent;

            var receiverTag = root.AttachComponent<ValueRegister<string>>();
            var varNameSlot = root.AttachComponent<ValueRegister<string>>();
            var varNameActive = root.AttachComponent<ValueRegister<string>>();

            var receiver = root.AttachComponent<DynamicImpulseReceiver>();

            var parentIsNull = root.AttachComponent<IsNullNode<Slot>>();
            var ifNullParent = root.AttachComponent<IfNode>();
            var dupContainer = root.AttachComponent<DuplicateSlot>();
            var setParentOfContainer = root.AttachComponent<SetParent>();
            var parentCoalesce = root.AttachComponent<NullCoalesce<Slot>>();

            var listCount = root.AttachComponent<ChildrenCount>();
            var forNode = root.AttachComponent<ForNode>();
            var getChild = root.AttachComponent<GetChild>();
            var readSlot = root.AttachComponent<ReadDynamicVariable<Slot>>();
            var readActive = root.AttachComponent<ReadDynamicVariable<bool>>();
            var ifActive = root.AttachComponent<IfNode>();
            var setParentOfImported = root.AttachComponent<SetParent>();
            var rootSlot = root.AttachComponent<RootSlot>();

            listRef.Target.Target = list;
            templateRef.Target.Target = template;

            receiverTag.Value.Value = DYN_IMPULSE_TAG;
            varNameSlot.Value.Value = DYN_VAR_ENTRY_SLOT;
            varNameActive.Value.Value = DYN_VAR_ENTRY_ACTIVE;

            receiver.Tag.TryConnectTo(receiverTag);

            parentIsNull.Instance.TryConnectTo(parentRef);
            ifNullParent.Condition.TryConnectTo(parentIsNull);

            dupContainer.Template.TryConnectTo(templateRef);

            setParentOfContainer.NewParent.TryConnectTo(rootSlot);
            setParentOfContainer.Instance.TryConnectTo(dupContainer.Duplicate);

            parentCoalesce.A.TryConnectTo(parentRef);
            parentCoalesce.B.TryConnectTo(dupContainer.Duplicate);

            listCount.Instance.TryConnectTo(listRef);

            forNode.Count.TryConnectTo(listCount);

            getChild.Instance.TryConnectTo(listRef);
            getChild.ChildIndex.TryConnectTo(forNode.Iteration);

            readSlot.Source.TryConnectTo(getChild);
            readActive.Source.TryConnectTo(getChild);
            readSlot.VariableName.TryConnectTo(varNameSlot);
            readActive.VariableName.TryConnectTo(varNameActive);

            ifActive.Condition.TryConnectTo(readActive.Value);

            setParentOfImported.NewParent.TryConnectTo(parentCoalesce);
            setParentOfImported.Instance.TryConnectTo(readSlot.Value);


            receiver.Impulse.Target = ifNullParent.Run;
            ifNullParent.True.Target = dupContainer.DoDuplicate;
            ifNullParent.False.Target = forNode.Run;

            dupContainer.OnDuplicated.Target = setParentOfContainer.DoSetParent;
            setParentOfContainer.OnDone.Target = forNode.Run;

            forNode.LoopIteration.Target = ifActive.Run;
            ifActive.True.Target = setParentOfImported.DoSetParent;


            receiverTag.RemoveAllLogixBoxes();
            LogixHelper.MoveUnder(receiverTag, root);
        }


        private void AddImportToUI(Slot imported)
        {
            UniLog.Log($"addImportToUI {imported.Name}");

            var builder = new UIBuilder(listRoot);
            builder.Style.PreferredHeight = 24f;
            builder.Style.MinHeight = 24f;

            var entry = builder.Next(imported.Name);
            builder.Nest();

            entry.AttachComponent<DynamicVariableSpace>().SpaceName.Value = DYN_VAR_SPACE;

            entry.CreateReferenceVariable(DYN_VAR_ENTRY_SLOT, imported);

            var activeVar = entry.AttachComponent<DynamicValueVariable<bool>>();
            activeVar.VariableName.Value = DYN_VAR_ENTRY_ACTIVE;
            activeVar.Value.Value = true;

            var checkbox = builder.Checkbox((LocaleString)imported.Name, state: true, labelFirst: false);

            checkbox.TargetState.Target = activeVar.Value;
        }

        private void Init(Slot root)
        {
            UniLog.Log($"init {root.Name}");
            firstRoot = root;
            listRoot = null;
            alreadyImported.Clear();
        }
    }
}
