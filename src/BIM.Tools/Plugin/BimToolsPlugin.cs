using System;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using BIM.Tools.UI;
using BIM.Tools.Clash;
using BIM.Tools.Health;
using BIM.Tools.Updates;

namespace BIM.Tools.Plugin
{
    [Plugin("BIM.Tools.Commands","BIMT",DisplayName="BIM Tools",ToolTip="Section Box, remembered clash setups and strict model sharing")]
    [RibbonLayout("BimToolsRibbon.xaml")]
    [RibbonTab("BIM_TOOLS_TAB",DisplayName="BIM Tools")]
    [Command("BIM_OPEN_FEDERATION_AUDIT",DisplayName="Federation Audit",ToolTip="Compare loaded assemblies and source models with a saved complete model list",Icon="Images\\BimFederationAudit16.png",LargeIcon="Images\\BimFederationAudit32.png",CallCanExecute=CallCanExecute.Always,LoadForCanExecute=true)]
    [Command("BIM_OPEN_SECTION_BOX",DisplayName="Section Box",Icon="Images\\BimSectionBox16.png",LargeIcon="Images\\BimSectionBox32.png",CallCanExecute=CallCanExecute.Always,LoadForCanExecute=true)]
    [Command("BIM_OPEN_MODEL_SHARING",DisplayName="Model Sharing",Icon="Images\\BimModelSharing16.png",LargeIcon="Images\\BimModelSharing32.png",CallCanExecute=CallCanExecute.Always,LoadForCanExecute=true)]
    [Command("BIM_OPEN_CLASH_SETUP",DisplayName="Clash Setup",Icon="Images\\BimClash16.png",LargeIcon="Images\\BimClash32.png",CallCanExecute=CallCanExecute.Always,LoadForCanExecute=true)]
    [Command("BIM_CHECK_UPDATES",DisplayName="Check Updates",Icon="Images\\BimTools16.png",LargeIcon="Images\\BimTools32.png",CallCanExecute=CallCanExecute.Always,LoadForCanExecute=true)]
    public sealed class BimToolsPlugin : CommandHandlerPlugin
    {
        public override int ExecuteCommand(string commandId,params string[] parameters)
        {
            try
            {
                if(commandId=="BIM_CHECK_UPDATES") UpdateWindow.Open();
                else if(commandId=="BIM_OPEN_FEDERATION_AUDIT") FederationAuditWindow.Open(Application.ActiveDocument);
                else if(commandId=="BIM_OPEN_SECTION_BOX") SectionBoxBuilderWindow.Open(Application.ActiveDocument,null,null,true);
                else if(commandId=="BIM_OPEN_MODEL_SHARING") ScopedModelSharingWindow.Open(Application.ActiveDocument);
                else if(commandId=="BIM_OPEN_CLASH_SETUP") ClashSetupWindow.Open(Application.ActiveDocument);
                return 0;
            }
            catch(Exception error){BimDialog.ShowError(null,"BIM Tools","The tool could not complete the operation.",error.ToString()); return -1;}
        }
        public override CommandState CanExecuteCommand(string commandId)
        {CommandState state=new CommandState(); state.IsVisible=commandId=="BIM_CHECK_UPDATES"||commandId=="BIM_OPEN_FEDERATION_AUDIT"||commandId=="BIM_OPEN_SECTION_BOX"||commandId=="BIM_OPEN_MODEL_SHARING"||commandId=="BIM_OPEN_CLASH_SETUP"; state.IsEnabled=state.IsVisible; return state;}
    }
}
