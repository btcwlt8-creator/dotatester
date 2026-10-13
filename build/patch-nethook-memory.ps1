param([Parameter(Mandatory=$true)][string]$SteamKitRoot)
$ErrorActionPreference = 'Stop'
$native = Join-Path $SteamKitRoot 'Resources/NetHook2/NetHook2'
$loggerPath = Join-Path $native 'logger.cpp'
$injectorPath = Join-Path $native 'injector.cpp'
$nethookPath = Join-Path $native 'nethook.cpp'
foreach ($p in @($loggerPath, $injectorPath, $nethookPath)) { if (!(Test-Path $p)) { throw "NetHook2 source not found: $p" } }
$nl = [Environment]::NewLine

$injector = [IO.File]::ReadAllText($injectorPath)
if ($injector -notmatch '#include <string\.h>') {
    $injector = $injector.Replace('#include <stdlib.h>', '#include <stdlib.h>' + $nl + '#include <string.h>')
}
if ($injector -notmatch 'DotaLobbyOverlay\.TargetWindow') {
    $injector = $injector.Replace('int FindSteamProcessID();', 'static HANDLE g_hDotaOverlayMapping = NULL;' + $nl + 'int FindSteamProcessID();')
    $needle = 'void CALLBACK Inject( HWND hWindow, HINSTANCE hInstance, LPSTR lpszCommandLine, int nCmdShow )' + $nl + '{'
    $insert = @'
void CALLBACK Inject( HWND hWindow, HINSTANCE hInstance, LPSTR lpszCommandLine, int nCmdShow )
{
    HWND hOverlayWindow = hWindow;
    if (lpszCommandLine != nullptr && strncmp(lpszCommandLine, "window=", 7) == 0)
    {
        char* end = nullptr;
        unsigned long long rawHwnd = _strtoui64(lpszCommandLine + 7, &end, 10);
        if (end != lpszCommandLine + 7 && *end == '\0' && rawHwnd != 0)
            hOverlayWindow = reinterpret_cast<HWND>(static_cast<ULONG_PTR>(rawHwnd));
        lpszCommandLine = const_cast<char*>("");
    }
    g_hDotaOverlayMapping = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(HWND), "Local\\DotaLobbyOverlay.TargetWindow");
    if (g_hDotaOverlayMapping != NULL)
    {
        void* pView = MapViewOfFile(g_hDotaOverlayMapping, FILE_MAP_WRITE, 0, 0, sizeof(HWND));
        if (pView != NULL) { *static_cast<HWND*>(pView) = hOverlayWindow; UnmapViewOfFile(pView); }
    }
'@
    if (!$injector.Contains($needle)) { throw 'Could not locate Inject() declaration for NetHook patch.' }
    $injector = $injector.Replace($needle, $insert)
    [IO.File]::WriteAllText($injectorPath, $injector, [Text.Encoding]::UTF8)
}

$nethook = [IO.File]::ReadAllText($nethookPath)
if ($nethook -notmatch 'DotaLobbyOverlay\.TargetWindow') {
    $nethook = $nethook.Replace('BOOL g_bOwnsConsole = FALSE;', 'BOOL g_bOwnsConsole = FALSE;' + $nl + 'HWND g_hDotaOverlayWindow = NULL;')
    $needle = 'GOOGLE_PROTOBUF_VERIFY_VERSION;'
    $insert = @'
HANDLE hOverlayMap = OpenFileMappingA(FILE_MAP_READ, FALSE, "Local\\DotaLobbyOverlay.TargetWindow");
        if (hOverlayMap != NULL)
        {
            const HWND* pOverlay = static_cast<const HWND*>(MapViewOfFile(hOverlayMap, FILE_MAP_READ, 0, 0, sizeof(HWND)));
            if (pOverlay != NULL) { g_hDotaOverlayWindow = *pOverlay; UnmapViewOfFile(pOverlay); }
            CloseHandle(hOverlayMap);
        }
        GOOGLE_PROTOBUF_VERIFY_VERSION;
'@
    if (!$nethook.Contains($needle)) { throw 'Could not locate DllMain attach block for NetHook patch.' }
    $nethook = $nethook.Replace($needle, $insert)
    [IO.File]::WriteAllText($nethookPath, $nethook, [Text.Encoding]::UTF8)
}

$logger = [IO.File]::ReadAllText($loggerPath)
if ($logger -notmatch 'DotaLobbyOverlay packet delivery') {
    $logger = $logger.Replace('#include "logger.h"', '#include "logger.h"' + $nl + 'extern HWND g_hDotaOverlayWindow;')
    $start = $logger.IndexOf('CLogger::CLogger() noexcept')
    $end = $logger.IndexOf('void CLogger::LogConsole', $start)
    if ($start -lt 0 -or $end -lt 0) { throw 'Could not locate CLogger constructor.' }
    $constructor = @'
CLogger::CLogger() noexcept
{
    m_uiMsgNum = 0;
}

'@
    $logger = $logger.Substring(0, $start) + $constructor + $logger.Substring($end)
    $start = $logger.IndexOf('void CLogger::LogSessionData(')
    $end = $logger.IndexOf('HANDLE CLogger::OpenFile', $start)
    if ($start -lt 0 -or $end -lt 0) { throw 'Could not locate LogSessionData.' }
    $memoryDelivery = @'
void CLogger::LogSessionData( ENetDirection eDirection, const uint8 *pData, uint32 cubData )
{
    // DotaLobbyOverlay packet delivery: no .bin files are written.
    if (g_hDotaOverlayWindow == NULL || pData == nullptr || cubData == 0) return;
    COPYDATASTRUCT copy = {};
    copy.dwData = static_cast<ULONG_PTR>(0x44524F53);
    copy.cbData = cubData;
    copy.lpData = const_cast<uint8*>(pData);
    DWORD_PTR result = 0;
    SendMessageTimeoutA(g_hDotaOverlayWindow, WM_COPYDATA, NULL, reinterpret_cast<LPARAM>(&copy), SMTO_ABORTIFHUNG, 100, &result);
}

'@
    $logger = $logger.Substring(0, $start) + $memoryDelivery + $logger.Substring($end)
    [IO.File]::WriteAllText($loggerPath, $logger, [Text.Encoding]::UTF8)
}
Write-Host 'Patched NetHook2: packet bytes are forwarded to the overlay in memory; session .bin writes are disabled.'
