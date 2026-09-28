using CampusCoin.Services;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace CampusCoin.ViewComponents;

public class SidebarNavViewComponent(AdminWorkspaceService workspace) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new SidebarNavViewModel
        {
            ShowAdminUi = workspace.ShowAdminUi,
            IsAdministrator = workspace.IsAdministrator,
            CurrentMode = workspace.CurrentMode,
            Permissions = await workspace.GetPermissionsAsync()
        };
        return View(model);
    }
}
