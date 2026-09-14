$code = @"
using System;
using System.Runtime.InteropServices;

public static class DesktopProcessLauncher {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation
    );
}
"@

if (-not ([System.Management.Automation.PSTypeName]'DesktopProcessLauncher').Type) {
    Add-Type -TypeDefinition $code
}

$si = New-Object DesktopProcessLauncher+STARTUPINFO
$si.cb = [System.Runtime.InteropServices.Marshal]::SizeOf($si)
$si.lpDesktop = "WinSta0\Default"
$pi = New-Object DesktopProcessLauncher+PROCESS_INFORMATION

$exePath = "C:\Users\felip\WhisperLowCost\publish\VoiceFlow.exe"
$workingDir = "C:\Users\felip\WhisperLowCost\publish"

$success = [DesktopProcessLauncher]::CreateProcess($exePath, $null, [IntPtr]::Zero, [IntPtr]::Zero, $false, 0, [IntPtr]::Zero, $workingDir, [ref]$si, [ref]$pi)

if ($success) {
    Write-Host "VoiceFlow started successfully on WinSta0\Default with PID: $($pi.dwProcessId)"
} else {
    $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
    Write-Error "Failed to start process. Win32 Error: $err"
}
