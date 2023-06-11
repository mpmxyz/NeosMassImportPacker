using BaseX;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.LogiX.Actions;
using FrooxEngine.LogiX.Data;
using FrooxEngine.LogiX.Input;
using FrooxEngine.LogiX.Operators;
using FrooxEngine.LogiX.ProgramFlow;
using FrooxEngine.LogiX.Undo;
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

        private const string UNDO_DESCRIPTION = "Pack assets";

        private const string DYN_IMPULSE_TAG_PACK = "pack";
        private const string DYN_IMPULSE_TAG_UPDATE = "update";
        private const string DYN_IMPULSE_TAG_SET = "set";
        private const string DYN_VAR_SPACE_NAME_ENTRY = "entry";
        private const string DYN_VAR_ENTRY_SLOT = "entry/slot";
        private const string DYN_VAR_ENTRY_ACTIVE = "entry/active";

        private const string NAME_WIZARD_ROOT = "Mass Import Packer";
        private const string NAME_TEMPLATES = "Templates";
        private const string NAME_IMPORT_PACKAGE = "Import Package";
        private const string NAME_LOGIX = "LogiX";
        private const string NAME_DESTROY_PROXY = "Import Packer Destroy Proxy";

        private const string TITLE_WIZARD = "Mass Import Packer";
        private const string LABEL_ALL = "All";
        private const string LABEL_CREATE_PARENT = "Pack into new Slot";
        private const string LABEL_REPARENT = "Reparent";
        private const string LABEL_PARENT = "Parent";

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
            lock (this)
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
                        floatQ rotation = floatQ.LookRotation((firstImported.GlobalPosition - world.LocalUserViewPosition) * new float3(1, 0, 1));
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
            var ui = parent.AddSlot(NAME_WIZARD_ROOT, NeosMassImportPackerMod.PersistUI);
            //no developer tag so you can close it again
            ui.AttachComponent<ObjectRoot>();
            ui.AttachComponent<Grabbable>();
            ui.GlobalPosition = position + rotation * float3.Backward * 0.1f * scale;
            ui.GlobalRotation = rotation;
            ui.GlobalScale = scale;

            var panel = ui.AttachComponent<NeosCanvasPanel>();
            panel.Canvas.Slot.Tag = "Developer";
            panel.Panel.Title = TITLE_WIZARD;
            panel.Panel.AddCloseButton();
            panel.CanvasSize = new float2(250, 500);
            panel.CanvasScale = 0.25f / panel.CanvasSize.x;

            var builder = new UIBuilder(panel.Canvas);
            builder.VerticalLayout(spacing: 4f);

            builder.Style.PreferredHeight = 24f;
            builder.Style.MinHeight = 24f;
            var selectAllCheckbox = builder.Checkbox(LABEL_ALL, labelFirst: false);
            var selectAllState = selectAllCheckbox.CheckVisual.Target;
            var selectTrigger = selectAllCheckbox.Slot.AttachComponent<ButtonDynamicImpulseTriggerWithValue<bool>>();
            selectTrigger.PressedData.Tag.Value = DYN_IMPULSE_TAG_SET;
            var allSelectedValueCopy = selectAllCheckbox.Slot.AttachComponent<ValueCopy<bool>>();
            var invertedState = selectAllCheckbox.Slot.AttachComponent<BooleanValueDriver<bool>>();
            allSelectedValueCopy.Source.Target = selectAllState;
            allSelectedValueCopy.Target.Target = invertedState.State;
            invertedState.TrueValue.Value = false;
            invertedState.FalseValue.Value = true;
            invertedState.TargetField.Target = selectTrigger.PressedData.Value;
            //reuse everything except the checkbox itself
            selectAllCheckbox.Destroy();

            builder.Style.FlexibleHeight = 1;
            builder.ScrollArea();

            builder.Style.ForceExpandHeight = false;
            var listRoot = builder.VerticalLayout(spacing: 4f, childAlignment: Alignment.TopLeft).Slot;
            selectTrigger.Target.Target = listRoot;
            builder.FitContent(horizontal: SizeFit.Disabled, vertical: SizeFit.MinSize);
            builder.NestOut();
            //builder.NestOut();

            builder.Style.FlexibleHeight = -1;
            builder.Style.PreferredHeight = 24f;

            var template = ui.AddSlot(NAME_TEMPLATES).AddSlot(NAME_IMPORT_PACKAGE);
            var logix = ui.AddSlot(NAME_LOGIX);
            CreateLogiX_Pack(logix, listRoot, template, out var parentRef);
            CreateLogiX_Update(logix, listRoot, selectAllState);
            SyncMemberEditorBuilder.Build(parentRef, LABEL_PARENT, null, builder);

            var button = builder.Button();
            var hasNoParent = button.Slot.AttachComponent<ReferenceEqualityDriver<Slot>>();
            var buttonTextDriver = button.Slot.AttachComponent<BooleanValueDriver<string>>();
            hasNoParent.TargetReference.Target = parentRef;
            hasNoParent.Target.Target = buttonTextDriver.State;
            buttonTextDriver.TargetField.Target = button.LabelTextField;
            buttonTextDriver.TrueValue.Value = LABEL_CREATE_PARENT;
            buttonTextDriver.FalseValue.Value = LABEL_REPARENT;

            builder.NestOut();

            var trigger = button.Slot.AttachComponent<ButtonDynamicImpulseTrigger>();
            trigger.Target.Target = logix;
            trigger.PressedTag.Value = DYN_IMPULSE_TAG_PACK;

            return listRoot;
        }


        private static Action CreateLogiX_ForEach(Slot root, IElementContent<Slot> loopParent, out IElementContent<Slot> child, Action onStart = null, Action onEach = null, Action onEnd = null)
        {
            var listCount = root.AttachComponent<ChildrenCount>();
            var forNode = root.AttachComponent<ForNode>();
            var getChild = root.AttachComponent<GetChild>();

            //values
            listCount.Instance.Target = loopParent;
            getChild.Instance.Target = loopParent;
            forNode.Count.Target = listCount;
            getChild.ChildIndex.Target = forNode.Iteration;

            //IO
            child = getChild;
            forNode.LoopStart.Target = onStart;
            forNode.LoopIteration.Target = onEach;
            forNode.LoopEnd.Target = onEnd;
            return forNode.Run;
        }

        private static IElementContent<T> CreateLogiX_ReadDynamicVariable<T>(Slot root, IElementContent<Slot> source, IElementContent<string> name)
        {
            var dv = root.AttachComponent<ReadDynamicVariable<T>>();
            dv.Source.Target = source;
            dv.VariableName.Target = name;
            return dv.Value;
        }

        private static void CreateLogiX_Pack(Slot root, Slot list, Slot template, out SyncRef<Slot> parentRef)
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
            var undoDescription = root.AttachComponent<ValueRegister<string>>();
            var createUndoBatch = root.AttachComponent<CreateUndoBatch>();

            var parentIsNull = root.AttachComponent<IsNullNode<Slot>>();
            var ifNullParent = root.AttachComponent<IfNode>();
            var dupContainer = root.AttachComponent<DuplicateSlot>();
            var createParentDupUndo = root.AttachComponent<CreateSpawnUndoStep>();
            var setParentOfContainer = root.AttachComponent<SetParent>();
            var setDupAsParent = root.AttachComponent<WriteValueNode<Slot>>();

            //loop:
            var ifActive = root.AttachComponent<IfNode>();
            var createTransformUndo = root.AttachComponent<CreateTransformUndoStep>();
            var setParentOfImported = root.AttachComponent<SetParent>();
            var rootSlot = root.AttachComponent<RootSlot>();

            var triggerLoop = CreateLogiX_ForEach(root, listReg, out var child, onEach: ifActive.Run);

            var entrySlot = CreateLogiX_ReadDynamicVariable<Slot>(root, child, varNameSlot);
            var entryActive = CreateLogiX_ReadDynamicVariable<bool>(root, child, varNameActive);

            //values
            listReg.Target.Target = list;
            templateReg.Target.Target = template;
            parentRegRef.Target.Target = parentReg;
            undoDescription.Value.Value = UNDO_DESCRIPTION;

            receiverTag.Value.Value = DYN_IMPULSE_TAG_PACK;
            varNameSlot.Value.Value = DYN_VAR_ENTRY_SLOT;
            varNameActive.Value.Value = DYN_VAR_ENTRY_ACTIVE;

            receiver.Tag.TryConnectTo(receiverTag);

            createUndoBatch.Description.Target = undoDescription;

            parentIsNull.Instance.TryConnectTo(parentReg);
            ifNullParent.Condition.TryConnectTo(parentIsNull);

            dupContainer.Template.TryConnectTo(templateReg);

            createParentDupUndo.Target.Target = dupContainer.Duplicate;

            setParentOfContainer.NewParent.TryConnectTo(rootSlot);
            setParentOfContainer.Instance.TryConnectTo(dupContainer.Duplicate);

            setDupAsParent.Value.TryConnectTo(dupContainer.Duplicate);
            setDupAsParent.Target.TryConnectTo(parentRegRef);

            ifActive.Condition.TryConnectTo(entryActive);

            createTransformUndo.Target.Target = entrySlot;

            setParentOfImported.Instance.TryConnectTo(entrySlot);
            setParentOfImported.NewParent.TryConnectTo(parentReg);

            //impulses
            receiver.Impulse.Target = createUndoBatch.DoCreate;
            createUndoBatch.Create.Target = ifNullParent.Run;
            ifNullParent.True.Target = dupContainer.DoDuplicate;
            ifNullParent.False.Target = triggerLoop;

            dupContainer.OnDuplicated.Target = createParentDupUndo.Create;
            createParentDupUndo.OnCreated.Target = setParentOfContainer.DoSetParent;
            setParentOfContainer.OnDone.Target = setDupAsParent.Write;
            setDupAsParent.OnDone.Target = triggerLoop;

            ifActive.True.Target = createTransformUndo.Create;
            createTransformUndo.OnCreated.Target = setParentOfImported.DoSetParent;

            //pack
            receiverTag.RemoveAllLogixBoxes();
            LogixHelper.MoveUnder(receiverTag, root);
        }

        private static void CreateLogiX_Update(Slot logixRoot, Slot listRoot, IValue<bool> allSelected)
        {
            var listRef = logixRoot.AttachComponent<ReferenceNode<Slot>>();
            var allSelectedRef = logixRoot.AttachComponent<ReferenceRegister<IValue<bool>>>();

            var localUser = logixRoot.AttachComponent<LocalUser>();
            var onChildrenEvents = logixRoot.AttachComponent<SlotChildrenEvents>();

            var updateTag = logixRoot.AttachComponent<ValueRegister<string>>();
            var updateReceiver = logixRoot.AttachComponent<DynamicImpulseReceiver>();

            var ifActive = logixRoot.AttachComponent<IfNode>();
            var trueValue = logixRoot.AttachComponent<BoolInput>();
            var latch = logixRoot.AttachComponent<WriteLatch<bool>>();

            var triggerLoop = CreateLogiX_ForEach(logixRoot, listRef, out var child, onStart: latch.Reset, onEach: ifActive.Run);

            var varNameActive = logixRoot.AttachComponent<ValueRegister<string>>();
            var readActive = CreateLogiX_ReadDynamicVariable<bool>(logixRoot, child, varNameActive);

            //values
            listRef.RefTarget.Target = listRoot;
            allSelectedRef.Target.Target = allSelected;
            updateTag.Value.Value = DYN_IMPULSE_TAG_UPDATE;
            varNameActive.Value.Value = DYN_VAR_ENTRY_ACTIVE;
            trueValue.Value.Value = true;

            onChildrenEvents.Instance.Target = listRef;
            onChildrenEvents.OnUser.Target = localUser;

            updateReceiver.Tag.Target = updateTag;

            ifActive.Condition.Target = readActive;
            latch.ResetValue.Target = trueValue;
            latch.Target.Target = allSelectedRef;

            //impulses
            onChildrenEvents.OnChildAdded.Target = triggerLoop;
            onChildrenEvents.OnChildRemoved.Target = triggerLoop;
            updateReceiver.Impulse.Target = triggerLoop;

            ifActive.False.Target = latch.Set;

            //pack
            onChildrenEvents.RemoveAllLogixBoxes();
            LogixHelper.MoveUnder(onChildrenEvents, logixRoot);
        }

        private static void AddImportToUI(Slot listRoot, Slot imported)
        {
            var builder = new UIBuilder(listRoot);
            builder.Style.PreferredHeight = 24f;
            builder.Style.MinHeight = 24f;

            var entry = builder.Next(imported.Name);
            entry.PersistentSelf = NeosMassImportPackerMod.PersistUI;
            builder.Nest();

            var dynSpace = entry.AttachComponent<DynamicVariableSpace>();
            dynSpace.SpaceName.Value = DYN_VAR_SPACE_NAME_ENTRY;
            var slotVar = entry.CreateReferenceVariable(DYN_VAR_ENTRY_SLOT, imported);
            //hack to maintain same order as imported assets even if imports take a different amount of time:
            entry.OrderOffset = ((long)(ulong)imported.ReferenceID) + long.MinValue;
            slotVar.UpdateLinking();

            var activeVar = entry.AttachComponent<DynamicValueVariable<bool>>();
            activeVar.VariableName.Value = DYN_VAR_ENTRY_ACTIVE;
            activeVar.Value.Value = true;
            activeVar.UpdateLinking();

            var checkbox = builder.Checkbox((LocaleString)imported.Name, state: true, labelFirst: false);

            checkbox.TargetState.Target = activeVar.Value;

            var destroyProxyToEntry = imported.AddSlot(NAME_DESTROY_PROXY, NeosMassImportPackerMod.PersistUI).DestroyWhenDestroyed(entry);
            entry.DestroyWhenDestroyed(destroyProxyToEntry.Slot);

            var logix = entry.AddSlot(NAME_LOGIX);
            AddEntryLogiX_Update(listRoot.GetObjectRoot(), logix);
            AddEntryLogiX_Set(logix);
        }

        private static void AddEntryLogiX_Update(Slot wizardRoot, Slot logixRoot)
        {
            var isActiveDrive = logixRoot.AttachComponent<DynamicValueVariableDriver<bool>>();
            var isActive = logixRoot.AttachComponent<ValueRegister<bool>>();
            var localFireOnChange = logixRoot.AttachComponent<LocalFireOnChange<bool>>();

            var updateTag = logixRoot.AttachComponent<ValueRegister<string>>();
            var updateRoot = logixRoot.AttachComponent<SlotRegister>();
            var triggerUpdate = logixRoot.AttachComponent<DynamicImpulseTrigger>();

            //values
            isActiveDrive.VariableName.Value = DYN_VAR_ENTRY_ACTIVE;
            isActiveDrive.Target.Target = isActive.Value;

            updateTag.Value.Value = DYN_IMPULSE_TAG_UPDATE;
            updateRoot.Target.Target = wizardRoot;

            localFireOnChange.Value.Target = isActive.Value;
            triggerUpdate.Tag.Target = updateTag;
            triggerUpdate.TargetHierarchy.Target = updateRoot;

            //impulses
            localFireOnChange.Pulse.Target = triggerUpdate.Run;

            //pack
            localFireOnChange.RemoveAllLogixBoxes();
            LogixHelper.MoveUnder(localFireOnChange, logixRoot);
        }

        private static void AddEntryLogiX_Set(Slot logixRoot)
        {
            var triggerTag = logixRoot.AttachComponent<ValueRegister<string>>();
            var varName = logixRoot.AttachComponent<ValueRegister<string>>();

            var trigger = logixRoot.AttachComponent<DynamicImpulseReceiverWithValue<bool>>();
            var writeVar = logixRoot.AttachComponent<WriteDynamicVariable<bool>>();

            //values
            triggerTag.Value.Value = DYN_IMPULSE_TAG_SET;
            varName.Value.Value = DYN_VAR_ENTRY_ACTIVE;

            trigger.Tag.Target = triggerTag;
            writeVar.VariableName.Target = varName;
            writeVar.Value.Target = trigger.Value;

            //impulses
            trigger.Impulse.Target = writeVar.Write;

            //pack
            trigger.RemoveAllLogixBoxes();
            LogixHelper.MoveUnder(trigger, logixRoot);
        }
    }
}
