#Requires AutoHotkey v2.0
; ==============================================================================
; WindowTileHotkeys für AutoHotkey v2
; Shortcuts:
;   Win + Alt + Strg + 2  ->  2 Fenster nebeneinander
;   Win + Alt + Strg + 3  ->  3 Fenster nebeneinander
;   Win + Alt + Strg + 4  ->  4 Fenster nebeneinander
; ==============================================================================

#SingleInstance Force

#!^2::TileWindows(2)
#!^3::TileWindows(3)
#!^4::TileWindows(4)

; Numpad Support
#!^Numpad2::TileWindows(2)
#!^Numpad3::TileWindows(3)
#!^Numpad4::TileWindows(4)

TileWindows(columnCount)
{
    ; Primären Monitor / Arbeitsfläche ermitteln
    MonitorGetWorkArea(1, &Left, &Top, &Right, &Bottom)
    workWidth := Right - Left
    workHeight := Bottom - Top
    colWidth := Floor(workWidth / columnCount)

    ; Fenster sammeln (bevorzugt Antigravity, sonst oberste sichtbare)
    candidateList := []
    allHwnds := WinGetList()

    for hwnd in allHwnds
    {
        title := WinGetTitle(hwnd)
        style := WinGetStyle(hwnd)
        exStyle := WinGetExStyle(hwnd)
        procName := WinGetProcessName(hwnd)

        ; Nur normale, sichtbare Anwendungsfenster
        if (title = "" || procName = "explorer.exe")
            continue
        if (exStyle & 0x00000080) ; WS_EX_TOOLWINDOW
            continue
        if !(style & 0x10000000) ; WS_VISIBLE
            continue

        candidateList.Push(hwnd)
        if (candidateList.Length >= columnCount)
            break
    }

    if (candidateList.Length < 2)
        return

    ; WinGetList() liefert Z-Order von oben nach unten (neuestes/aktives Fenster zuerst).
    ; Die Windows-Taskleiste ordnet Thumbnails jedoch chronologisch (FIFO: ältestes zuerst = links).
    ; Durch Umkehren der Liste entspricht Spalte 0 (links) exakt dem linken Taskleisten-Thumbnail.
    reversedList := []
    Loop candidateList.Length
        reversedList.Push(candidateList[candidateList.Length - A_Index + 1])
    candidateList := reversedList

    actualCols := Min(columnCount, candidateList.Length)

    for index, hwnd in candidateList
    {
        if (index > actualCols)
            break

        ; Wiederherstellen falls minimiert oder maximiert
        minMax := WinGetMinMax(hwnd)
        if (minMax != 0)
            WinRestore(hwnd)

        x := Left + ((index - 1) * colWidth)
        y := Top
        w := (index = actualCols) ? (workWidth - ((actualCols - 1) * colWidth)) : colWidth
        h := workHeight

        WinMove(x, y, w, h, hwnd)
    }
}
