Attribute VB_Name = "Excel_Consolidare"
' =============================================================================
' Modul:    Excel_Consolidare
' Scop:     Aduna intr-o singura foaie datele din toate fisierele Excel dintr-un
'           folder (aceeasi structura de coloane), cu o coloana "Fisier sursa".
' Rulare:   Alt+F8 > Consolidare > alegeti folderul. Fisierele sursa se deschid
'           doar pentru citire, cu macrourile lor dezactivate. Registrul cu
'           macroul si registrele deschise acum se sar (raportate la final).
' Modifica: doar foaia "Consolidat" din acest registru (o creeaza; daca exista,
'           cere confirmare inainte de a o inlocui). Sursele nu se modifica.
' Versiune: 1.1
' =============================================================================
Option Explicit

Private Const FOAIE_REZULTAT As String = "Consolidat"
Private Const RAND_ANTET As Long = 1
Private Const COL_SURSA As String = "Fi{s,}ier surs{a~}" ' scris prin RO()

' Macroul pentru utilizator: alege folderul, confirma, afiseaza rezultatul.
Public Sub Consolidare()
    Dim folder As String, inlocuieste As Boolean, rez As String
    On Error GoTo Eroare
    With Application.FileDialog(4) ' msoFileDialogFolderPicker
        .Title = "Alegeti folderul cu fisierele de consolidat"
        If .Show <> -1 Then Exit Sub
        folder = .SelectedItems(1)
    End With
    If FoaieExista(ThisWorkbook, FOAIE_REZULTAT) Then
        If MsgBox("Foaia '" & FOAIE_REZULTAT & "' exista deja. O inlocuiesc?", _
                  vbYesNo + vbQuestion, "Consolidare") <> vbYes Then Exit Sub
        inlocuieste = True
    End If
    rez = ConsolideazaFolder(folder, "", inlocuieste)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Consolidare"
    Exit Sub
Eroare:
    MsgBox "Consolidarea nu a reusit: " & Err.Description, vbCritical, "Consolidare"
End Sub

' Logica, fara dialoguri (se poate testa/apela din alt cod).
' numeFoaie = "" -> prima foaie din fiecare fisier. Intoarce "OK: ..." sau "EROARE: ...".
Public Function ConsolideazaFolder(ByVal folder As String, ByVal numeFoaie As String, _
                                  ByVal inlocuieste As Boolean) As String
    Dim calc As Long, secAuto As Long, fisier As String, wb As Workbook, ws As Worksheet
    Dim date_ As Variant, antet As Variant, blocuri As New Collection, surse As New Collection
    Dim sarite As String, nrFis As Long, totalRanduri As Long, nrCol As Long
    Dim rez() As Variant, bloc As Variant, i As Long, j As Long, k As Long, r As Long
    Dim wsOut As Worksheet, deschise As String

    If Right$(folder, 1) <> "\" Then folder = folder & "\"
    If Len(Dir(folder, vbDirectory)) = 0 Then
        ConsolideazaFolder = "EROARE: folderul nu exista: " & folder
        Exit Function
    End If
    If FoaieExista(ThisWorkbook, FOAIE_REZULTAT) And Not inlocuieste Then
        ConsolideazaFolder = "EROARE: foaia '" & FOAIE_REZULTAT & "' exista; nu o inlocuiesc fara confirmare."
        Exit Function
    End If

    calc = Application.Calculation
    secAuto = Application.AutomationSecurity
    On Error GoTo Eroare
    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.DisplayAlerts = False
    Application.Calculation = xlCalculationManual
    Application.AutomationSecurity = 3 ' msoAutomationSecurityForceDisable: macrourile surselor nu ruleaza

    fisier = Dir(folder & "*.xls*")
    Do While Len(fisier) > 0
        If Left$(fisier, 2) = "~$" Then
            ' fisier de blocare Office: nu e registru
        ElseIf RegistruDeschis(folder & fisier, fisier) Then
            deschise = deschise & vbLf & "  " & fisier
        Else
            Set wb = Workbooks.Open(Filename:=folder & fisier, UpdateLinks:=0, ReadOnly:=True, AddToMru:=False)
            Set ws = Nothing
            date_ = Empty
            If Len(numeFoaie) = 0 Then
                Set ws = wb.Worksheets(1)
            ElseIf FoaieExista(wb, numeFoaie) Then
                Set ws = wb.Worksheets(numeFoaie)
            End If
            If ws Is Nothing Then
                sarite = sarite & vbLf & fisier & " (lipseste foaia " & numeFoaie & ")"
            ElseIf Application.WorksheetFunction.CountA(ws.Rows(RAND_ANTET)) = 0 Then
                sarite = sarite & vbLf & fisier & " (foaie goala)"
            Else
                date_ = ws.Cells(RAND_ANTET, 1).CurrentRegion.Value
                If Not IsArray(date_) Then date_ = Empty
            End If
            If IsEmpty(date_) Or ws Is Nothing Then
                ' deja raportat mai sus sau o singura celula: nimic de copiat
            Else
                If IsEmpty(antet) Then
                    nrCol = UBound(date_, 2)
                    ReDim antet(1 To nrCol)
                    For j = 1 To nrCol
                        antet(j) = date_(1, j)
                    Next j
                End If
                If Not AntetIdentic(date_, antet) Then
                    sarite = sarite & vbLf & fisier & " (alte coloane decat primul fisier)"
                ElseIf UBound(date_, 1) > 1 Then
                    blocuri.Add date_
                    surse.Add fisier
                    totalRanduri = totalRanduri + UBound(date_, 1) - 1
                    nrFis = nrFis + 1
                End If
            End If
            wb.Close SaveChanges:=False
            Set wb = Nothing
        End If
        fisier = Dir()
    Loop

    If nrFis = 0 Then
        ConsolideazaFolder = "EROARE: niciun fisier cu date in " & folder & sarite & _
            IIf(Len(deschise) > 0, vbLf & "Sarite (deschise acum):" & deschise, "")
        GoTo Curatare
    End If

    ' Un singur tablou in memorie, scris o singura data in foaie (rapid).
    ReDim rez(1 To totalRanduri + 1, 1 To nrCol + 1)
    For j = 1 To nrCol
        rez(1, j) = antet(j)
    Next j
    rez(1, nrCol + 1) = RO(COL_SURSA)
    r = 1
    For k = 1 To blocuri.Count
        bloc = blocuri(k)
        For i = 2 To UBound(bloc, 1)
            r = r + 1
            For j = 1 To nrCol
                rez(r, j) = bloc(i, j)
            Next j
            rez(r, nrCol + 1) = surse(k)
        Next i
    Next k

    If FoaieExista(ThisWorkbook, FOAIE_REZULTAT) Then ThisWorkbook.Worksheets(FOAIE_REZULTAT).Delete
    Set wsOut = ThisWorkbook.Worksheets.Add(After:=ThisWorkbook.Worksheets(ThisWorkbook.Worksheets.Count))
    wsOut.Name = FOAIE_REZULTAT
    wsOut.Cells(1, 1).Resize(UBound(rez, 1), UBound(rez, 2)).Value = rez
    wsOut.Rows(1).Font.Bold = True
    wsOut.Columns.AutoFit

    ConsolideazaFolder = "OK: " & totalRanduri & " randuri din " & nrFis & " fisiere in foaia '" & _
                         FOAIE_REZULTAT & "'." & IIf(Len(sarite) > 0, vbLf & "Sarite:" & sarite, "") & _
                         IIf(Len(deschise) > 0, vbLf & "Sarite (deschise acum in Excel, inclusiv registrul cu macroul):" & deschise, "")

Curatare:
    Application.AutomationSecurity = secAuto
    Application.Calculation = calc
    Application.DisplayAlerts = True
    Application.EnableEvents = True
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    ConsolideazaFolder = "EROARE: " & Err.Description & IIf(Len(fisier) > 0, " (fisier: " & fisier & ")", "")
    On Error Resume Next
    If Not wb Is Nothing Then wb.Close SaveChanges:=False
    On Error GoTo 0
    Resume Curatare
End Function

Private Function FoaieExista(ByVal wb As Workbook, ByVal nume As String) As Boolean
    Dim ws As Worksheet
    For Each ws In wb.Worksheets
        If StrComp(ws.Name, nume, vbTextCompare) = 0 Then FoaieExista = True: Exit Function
    Next ws
End Function

Private Function AntetIdentic(ByVal date_ As Variant, ByVal antet As Variant) As Boolean
    Dim j As Long
    If UBound(date_, 2) <> UBound(antet) Then Exit Function
    For j = 1 To UBound(date_, 2)
        If StrComp(Trim$(CStr(date_(1, j))), Trim$(CStr(antet(j))), vbTextCompare) <> 0 Then Exit Function
    Next j
    AntetIdentic = True
End Function

' Registrul cu macroul, un registru deschis acum sau unul cu acelasi nume (Excel nu
' deschide doua registre cu acelasi nume) nu se deschide si nu se inchide.
Private Function RegistruDeschis(ByVal cale As String, ByVal nume As String) As Boolean
    Dim w As Workbook
    If StrComp(cale, ThisWorkbook.FullName, vbTextCompare) = 0 Then RegistruDeschis = True: Exit Function
    For Each w In Workbooks
        If StrComp(w.FullName, cale, vbTextCompare) = 0 Or StrComp(w.Name, nume, vbTextCompare) = 0 Then
            RegistruDeschis = True
            Exit Function
        End If
    Next w
End Function

' Text cu diacritice pentru celule si documente, scris in cod doar cu litere ASCII
' (editorul VBA citeste .bas in pagina de cod ANSI). Jetoane: {a~} {A~} = a/A cu
' caciula rotunda, {a^} {A^} {i^} {I^} = cu caciula ascutita, {s,} {S,} {t,} {T,}
' = cu virgula dedesubt. MsgBox si InputBox raman fara diacritice: sunt ANSI.
Private Function RO(ByVal s As String) As String
    Dim p As Variant, kv() As String
    For Each p In Split("a~:103 A~:102 a^:E2 A^:C2 i^:EE I^:CE s,:219 S,:218 t,:21B T,:21A", " ")
        kv = Split(p, ":")
        s = Replace(s, "{" & kv(0) & "}", ChrW(CLng("&H" & kv(1))))
    Next p
    RO = s
End Function
