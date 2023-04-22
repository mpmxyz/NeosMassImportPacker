using BaseX;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.LogiX.Data;
using FrooxEngine.LogiX.Input;
using FrooxEngine.LogiX.Meta;
using FrooxEngine.LogiX.ProgramFlow;
using FrooxEngine.LogiX.WorldModel;
using FrooxEngine.UIX;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

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
            ui.AttachComponent<Grabbable>();
            ui.GlobalPosition = firstRoot.GlobalPosition + firstRoot.Backward * 0.1f;
            ui.GlobalRotation = firstRoot.GlobalRotation;

            var panel = ui.AttachComponent<NeosCanvasPanel>();
            panel.Panel.Title = "Mass Import Packer";
            panel.Panel.AddCloseButton();
            panel.CanvasSize = new float2(250, 500);
            panel.CanvasScale = 0.25f / panel.CanvasSize.x;

            var builder = new UIBuilder(panel.Canvas);
            builder.VerticalLayout();

            builder.Style.FlexibleHeight = 1;
            builder.ScrollArea();

            builder.Style.ForceExpandHeight = false;
            listRoot = builder.VerticalLayout(childAlignment: Alignment.TopLeft).Slot;
            builder.FitContent(horizontal: SizeFit.Disabled, vertical: SizeFit.MinSize);
            builder.NestOut();
            //builder.NestOut();

            builder.Style.FlexibleHeight = -1;
            builder.Style.PreferredHeight = 24f;
            
            var button = builder.Button((LocaleString) "Pack into new Slot");
            builder.NestOut();

            var template = ui.AddSlot("Templates").AddSlot("Import Package");

            var logix = ui.AddSlot("LogiX");
            CreateLogiX(logix, listRoot, template);

            var trigger = button.Slot.AttachComponent<ButtonDynamicImpulseTrigger>();
            trigger.Target.Target = logix;
            trigger.PressedTag.Value = DYN_IMPULSE_TAG;

        }

        private static void CreateLogiX(Slot parent, Slot list, Slot template)
        {
            var listRef = parent.AttachComponent<ReferenceRegister<Slot>>();
            var templateRef = parent.AttachComponent<ReferenceRegister<Slot>>();

            var receiverTag = parent.AttachComponent<ValueRegister<string>>();
            var varNameSlot = parent.AttachComponent<ValueRegister<string>>();
            var varNameActive = parent.AttachComponent<ValueRegister<string>>();

            var receiver = parent.AttachComponent<DynamicImpulseReceiver>();
            var listCount = parent.AttachComponent<ChildrenCount>();
            var forNode = parent.AttachComponent<ForNode>();
            var getChild = parent.AttachComponent<GetChild>();
            var readSlot = parent.AttachComponent<ReadDynamicVariable<Slot>>();
            var readActive = parent.AttachComponent<ReadDynamicVariable<bool>>();
            var ifNode = parent.AttachComponent<IfNode>();
            var dupContainer = parent.AttachComponent<DuplicateSlot>();
            var setParentOfContainer = parent.AttachComponent<SetParent>();
            var setParentOfImported = parent.AttachComponent<SetParent>();
            var rootSlot = parent.AttachComponent<RootSlot>();

            listRef.Target.Target = list;
            templateRef.Target.Target = template;

            receiverTag.Value.Value = DYN_IMPULSE_TAG;
            varNameSlot.Value.Value = DYN_VAR_ENTRY_SLOT;
            varNameActive.Value.Value = DYN_VAR_ENTRY_ACTIVE;


            receiver.Tag.TryConnectTo(receiverTag);

            dupContainer.Template.TryConnectTo(templateRef);

            setParentOfContainer.NewParent.TryConnectTo(rootSlot);
            setParentOfContainer.Instance.TryConnectTo(dupContainer.Duplicate);

            listCount.Instance.TryConnectTo(listRef);

            forNode.Count.TryConnectTo(listCount);

            getChild.Instance.TryConnectTo(listRef);
            getChild.ChildIndex.TryConnectTo(forNode.Iteration);

            readSlot.Source.TryConnectTo(getChild);
            readActive.Source.TryConnectTo(getChild);
            readSlot.VariableName.TryConnectTo(varNameSlot);
            readActive.VariableName.TryConnectTo(varNameActive);

            ifNode.Condition.TryConnectTo(readActive.Value);

            setParentOfImported.NewParent.TryConnectTo(dupContainer.Duplicate);
            setParentOfImported.Instance.TryConnectTo(readSlot.Value);

            receiver.Impulse.Target = dupContainer.DoDuplicate;
            dupContainer.OnDuplicated.Target = setParentOfContainer.DoSetParent;
            setParentOfContainer.OnDone.Target = forNode.Run;
            forNode.LoopIteration.Target = ifNode.Run;
            ifNode.True.Target = setParentOfImported.DoSetParent;

            receiverTag.RemoveAllLogixBoxes();
            LogixHelper.MoveUnder(receiverTag, parent);
        }


        private void AddImportToUI(Slot imported)
        {
            UniLog.Log($"addImportToUI {imported.Name}");

            var builder = new UIBuilder(listRoot);
            builder.Style.PreferredHeight = 24f;
            builder.Style.MinHeight = 24f;

            
            var entry = builder.Next(imported.Name);

            entry.AttachComponent<DynamicVariableSpace>().SpaceName.Value = DYN_VAR_SPACE;

            entry.CreateReferenceVariable(DYN_VAR_ENTRY_SLOT, imported);

            var activeVar = entry.AttachComponent<DynamicValueVariable<bool>>();
            activeVar.VariableName.Value = DYN_VAR_ENTRY_ACTIVE;
            activeVar.Value.Value = true;


            var checkbox = builder.Checkbox((LocaleString) imported.Name, state: true, labelFirst: false);

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
