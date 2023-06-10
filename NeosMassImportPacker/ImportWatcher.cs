using BaseX;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.LogiX.Actions;
using FrooxEngine.LogiX.Data;
using FrooxEngine.LogiX.Operators;
using FrooxEngine.LogiX.ProgramFlow;
using FrooxEngine.LogiX.WorldModel;
using FrooxEngine.UIX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosMassImportPacker
{
    internal class ImportWatcher
    {
        private const int MIN_COUNT = 2;

        private const string DYN_IMPULSE_TAG = "pack";
        private const string DYN_VAR_SPACE = "entry";
        private const string DYN_VAR_ENTRY_SLOT = "entry/slot";
        private const string DYN_VAR_ENTRY_ACTIVE = "entry/active";

        private Slot listRoot = null;
        private DateTime lastUpdate = DateTime.UtcNow;
        private readonly IList<Slot> alreadyImported = new List<Slot>();

        internal void ResetImportGroup()
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan gap = now - lastUpdate;
            lastUpdate = now;

            if (gap.TotalSeconds > NeosMassImportPackerMod.MaxGap)
            {
                Clear();
            }
        }

        internal void Clear()
        {
            lock(this)
            {
                listRoot = null;
                alreadyImported.Clear();
            }
        }

        internal void OnImport(Slot imported)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan gap = now - lastUpdate;
            lock (this)
            {
                lastUpdate = now;
                alreadyImported.Add(imported);

                if (alreadyImported.Count >= MIN_COUNT)
                {
                    if (listRoot == null)
                    {
                        Slot firstImported = alreadyImported.OrderBy(slot => slot.ReferenceID).First();
                        World world = firstImported.World;
                        floatQ rotation = floatQ.LookRotation(firstImported.GlobalPosition - world.LocalUserViewPosition);
                        listRoot = SpawnUI(world.LocalUserSpace, firstImported.GlobalPosition, rotation, world.LocalUserGlobalScale);
                        listRoot.OnPrepareDestroy += (oldRoot) => Clear();

                        foreach (Slot slot in alreadyImported)
                        {
                            AddImportToUI(listRoot, slot);
                        }
                    }
                    else
                    {
                        AddImportToUI(listRoot, imported);
                    }
                }
            }
        }

        private static Slot SpawnUI(Slot parent, float3 position, floatQ rotation, float3 scale)
        {
            var ui = parent.AddSlot("Mass Import Packer", NeosMassImportPackerMod.PersistUI);
            //no developer tag so you can close it again
            ui.AttachComponent<Grabbable>();
            ui.GlobalPosition = position + rotation * float3.Backward * 0.1f * scale;
            ui.GlobalRotation = rotation;
            ui.GlobalScale = scale;

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
            var listRoot = builder.VerticalLayout(spacing: 4f, childAlignment: Alignment.TopLeft).Slot;
            builder.FitContent(horizontal: SizeFit.Disabled, vertical: SizeFit.MinSize);
            builder.NestOut();
            //builder.NestOut();

            builder.Style.FlexibleHeight = -1;
            builder.Style.PreferredHeight = 24f;

            var template = ui.AddSlot("Templates").AddSlot("Import Package");
            var logix = ui.AddSlot("LogiX");
            CreateLogiX(logix, listRoot, template, out var parentRef);
            SyncMemberEditorBuilder.Build(parentRef, "Parent", null, builder);

            var button = builder.Button();
            var hasNoParent = button.Slot.AttachComponent<ReferenceEqualityDriver<Slot>>();
            var buttonTextDriver = button.Slot.AttachComponent<BooleanValueDriver<string>>();
            hasNoParent.TargetReference.Target = parentRef;
            hasNoParent.Target.Target = buttonTextDriver.State;
            buttonTextDriver.TargetField.Target = button.LabelTextField;
            buttonTextDriver.TrueValue.Value = "Pack into new Slot";
            buttonTextDriver.FalseValue.Value = "Reparent";

            builder.NestOut();

            var trigger = button.Slot.AttachComponent<ButtonDynamicImpulseTrigger>();
            trigger.Target.Target = logix;
            trigger.PressedTag.Value = DYN_IMPULSE_TAG;

            return listRoot;
        }

        private static void CreateLogiX(Slot root, Slot list, Slot template, out SyncRef<Slot> parentRef)
        {
            var listReg = root.AttachComponent<ReferenceRegister<Slot>>();
            var templateReg = root.AttachComponent<ReferenceRegister<Slot>>();
            var parentReg = root.AttachComponent<SlotRegister>();
            var parentRegRef = root.AttachComponent<ReferenceRegister<IValue<Slot>>>();
            parentRef = parentReg.Target;

            var receiverTag = root.AttachComponent<ValueRegister<string>>();
            var varNameSlot = root.AttachComponent<ValueRegister<string>>();
            var varNameActive = root.AttachComponent<ValueRegister<string>>();

            var receiver = root.AttachComponent<DynamicImpulseReceiver>();

            var parentIsNull = root.AttachComponent<IsNullNode<Slot>>();
            var ifNullParent = root.AttachComponent<IfNode>();
            var dupContainer = root.AttachComponent<DuplicateSlot>();
            var setParentOfContainer = root.AttachComponent<SetParent>();
            var setDupAsParent = root.AttachComponent<WriteValueNode<Slot>>();

            var listCount = root.AttachComponent<ChildrenCount>();
            var forNode = root.AttachComponent<ForNode>();
            var getChild = root.AttachComponent<GetChild>();
            var readSlot = root.AttachComponent<ReadDynamicVariable<Slot>>();
            var readActive = root.AttachComponent<ReadDynamicVariable<bool>>();
            var ifActive = root.AttachComponent<IfNode>();
            var setParentOfImported = root.AttachComponent<SetParent>();
            var rootSlot = root.AttachComponent<RootSlot>();

            //values
            listReg.Target.Target = list;
            templateReg.Target.Target = template;
            parentRegRef.Target.Target = parentReg;

            receiverTag.Value.Value = DYN_IMPULSE_TAG;
            varNameSlot.Value.Value = DYN_VAR_ENTRY_SLOT;
            varNameActive.Value.Value = DYN_VAR_ENTRY_ACTIVE;

            receiver.Tag.TryConnectTo(receiverTag);

            parentIsNull.Instance.TryConnectTo(parentReg);
            ifNullParent.Condition.TryConnectTo(parentIsNull);

            dupContainer.Template.TryConnectTo(templateReg);

            setParentOfContainer.NewParent.TryConnectTo(rootSlot);
            setParentOfContainer.Instance.TryConnectTo(dupContainer.Duplicate);

            setDupAsParent.Value.TryConnectTo(dupContainer.Duplicate);
            setDupAsParent.Target.TryConnectTo(parentRegRef);

            listCount.Instance.TryConnectTo(listReg);

            forNode.Count.TryConnectTo(listCount);

            getChild.Instance.TryConnectTo(listReg);
            getChild.ChildIndex.TryConnectTo(forNode.Iteration);

            readSlot.Source.TryConnectTo(getChild);
            readActive.Source.TryConnectTo(getChild);
            readSlot.VariableName.TryConnectTo(varNameSlot);
            readActive.VariableName.TryConnectTo(varNameActive);

            ifActive.Condition.TryConnectTo(readActive.Value);

            setParentOfImported.NewParent.TryConnectTo(parentReg);
            setParentOfImported.Instance.TryConnectTo(readSlot.Value);

            //impulses
            receiver.Impulse.Target = ifNullParent.Run;
            ifNullParent.True.Target = dupContainer.DoDuplicate;
            ifNullParent.False.Target = forNode.Run;

            dupContainer.OnDuplicated.Target = setParentOfContainer.DoSetParent;
            setParentOfContainer.OnDone.Target = setDupAsParent.Write;
            setDupAsParent.OnDone.Target = forNode.Run;

            forNode.LoopIteration.Target = ifActive.Run;
            ifActive.True.Target = setParentOfImported.DoSetParent;

            //pack
            receiverTag.RemoveAllLogixBoxes();
            LogixHelper.MoveUnder(receiverTag, root);
        }


        private static void AddImportToUI(Slot listRoot, Slot imported)
        {
            var builder = new UIBuilder(listRoot);
            builder.Style.PreferredHeight = 24f;
            builder.Style.MinHeight = 24f;

            var entry = builder.Next(imported.Name);
            entry.PersistentSelf = NeosMassImportPackerMod.PersistUI;
            builder.Nest();

            entry.AttachComponent<DynamicVariableSpace>().SpaceName.Value = DYN_VAR_SPACE;
            entry.CreateReferenceVariable(DYN_VAR_ENTRY_SLOT, imported);
            //hack to maintain same order as imported assets even if imports take a different amount of time:
            entry.OrderOffset = ((long) (ulong) imported.ReferenceID) + long.MinValue;

            var activeVar = entry.AttachComponent<DynamicValueVariable<bool>>();
            activeVar.VariableName.Value = DYN_VAR_ENTRY_ACTIVE;
            activeVar.Value.Value = true;

            var checkbox = builder.Checkbox((LocaleString)imported.Name, state: true, labelFirst: false);

            checkbox.TargetState.Target = activeVar.Value;

            var destroyProxyToEntry = imported.AddSlot("Import Packer Destroy Proxy", NeosMassImportPackerMod.PersistUI).DestroyWhenDestroyed(entry);
            entry.DestroyWhenDestroyed(destroyProxyToEntry.Slot);
        }
    }
}
