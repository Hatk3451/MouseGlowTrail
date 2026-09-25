# CPU cycles per second of the given processes over the same interval (QueryProcessCycleTime).
param([Parameter(Mandatory)][string]$Ids, [int]$Seconds = 10)
$list = $Ids.Split(", ".ToCharArray(), [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [int]$_ }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Cycles
{
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static ulong Read(int pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        ulong cycles;
        QueryProcessCycleTime(handle, out cycles);
        CloseHandle(handle);
        return cycles;
    }
}
'@
$before = @{}
foreach ($id in $list) { $before[$id] = [Cycles]::Read($id) }
Start-Sleep -Seconds $Seconds
foreach ($id in $list) {
    $delta = [Cycles]::Read($id) - $before[$id]
    '{0,6}: {1,8:N2} Mcycles/s' -f $id, ($delta / 1e6 / $Seconds)
}
