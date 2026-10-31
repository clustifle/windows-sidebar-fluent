using System.Runtime.InteropServices;

namespace Sidebar;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class InstalledApp
{
    public string Name { get; set; }
    public string Target { get; set; }
    public InstalledApp(string name, string target) { Name = name; Target = target; }
}

internal static class InstalledApps
{
    private static Task<IReadOnlyList<InstalledApp>>? cached;
    public static Task<IReadOnlyList<InstalledApp>> GetAsync() => cached ??= RunSta<IReadOnlyList<InstalledApp>>(() =>
    {
        var result = new List<InstalledApp>();
        object? shell = null, folder = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            items = ((dynamic)folder!).Items();
            int count = ((dynamic)items).Count;
            for (int i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    string name = Convert.ToString(((dynamic)item!).Name) ?? "";
                    string id = Convert.ToString(((dynamic)item!).ExtendedProperty("System.AppUserModel.ID")) ?? "";
                    string target = id.Length > 0 ? "shell:AppsFolder\\" + id : Convert.ToString(((dynamic)item!).Path) ?? "";
                    if (name.Length > 0 && target.Length > 0) result.Add(new(name, target));
                }
                catch (COMException) { }
                finally { Release(item); }
            }
        }
        finally { Release(items); Release(folder); Release(shell); }
        return result.DistinctBy(a => a.Target, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    });

    internal static Task<T> RunSta<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(operation()); } catch (Exception ex) { completion.SetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
