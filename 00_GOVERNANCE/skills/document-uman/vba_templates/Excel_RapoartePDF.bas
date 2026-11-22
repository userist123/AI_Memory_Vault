Attribute VB_Name = "Excel_RapoartePDF"
' =============================================================================
' Modul:    Excel_RapoartePDF
' Scop:     Pentru fiecare valoare distincta dintr-o coloana (apartament, client,
'           angajat) face un raport separat cu randurile ei si un total, salvat
'           ca PDF: <prefix>_<valoare>.pdf (valorile care dau acelasi nume de
'           fisier, ex. "14/B" si "14_B", primesc _2, _3 ...).
' Rulare:   Alt+F8 > RapoartePDF. Datele sunt in foaia "Date", antet pe randul 1.
' Modifica: creeaza PDF-uri in folderul ales (fisierele existente se sar, daca
'           nu confirmati suprascrierea). Foaia temporara de lucru se sterge la
'           final; foaia "Date" nu se modifica.
' Versiune: 1.1
' =============================================================================
Option Explicit

Private Const FOAIE_DATE As String = "Date"
Private Const FOAIE_TEMP As String = "_RaportTemp"
Private Const TITLU As String = "Situa{t,}ie pentru: " ' scris prin RO()

Public Sub RapoartePDF()
    Dim folder As String, camp As String, campSuma As String, rasp As VbMsgBoxResult, rez As String
    On Error GoTo Eroare
    camp = InputBox("Coloana dupa care se imparte (numele din antet):", "Rapoarte PDF", "Apartament")
    If Len(camp) = 0 Then Exit Sub
    campSuma = InputBox("Coloana de totalizat (gol = fara total):", "Rapoarte PDF", "Suma")
    With Application.FileDialog(4)
        .Title = "Folderul in care salvez PDF-urile"
        If .Show <> -1 Then Exit Sub
        folder = .SelectedItems(1)
    End With
    rasp = MsgBox("Daca exista deja un PDF cu acelasi nume, il suprascriu?" & vbLf & _
                  "Nu = il pastrez si trec mai departe.", vbYesNoCancel + vbQuestion, "Rapoarte PDF")
    If rasp = vbCancel Then Exit Sub
    rez = GenereazaRapoarte(camp, campSuma, folder, "Raport", rasp = vbYes)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Rapoarte PDF"
    Exit Sub
Eroare:
    MsgBox "Rapoartele nu au fost generate: " & Err.Description, vbCritical, "Rapoarte PDF"
End Sub

Public Function GenereazaRapoarte(ByVal camp As String, ByVal campSuma As String, ByVal folder As String, _
                                  ByVal prefix As String, ByVal suprascrie As Boolean) As String
    Dim ws As Worksheet, wsT As Worksheet, date_ As Variant, colKey As Long, colSuma As Long
    Dim chei As Object, cheie As Variant, i As Long, j As Long, n As Long, r As Long
    Dim bloc() As Variant, total As Double, fis As String, facute As Long, sarite As Long
    Dim folosite As Object, nume As String, baza As String, k As Long

    If Right$(folder, 1) <> "\" Then folder = folder & "\"
    If Len(Dir(folder, vbDirectory)) = 0 Then
        GenereazaRapoarte = "EROARE: folderul nu exista: " & folder
        Exit Function
    End If
    If Not FoaieExista(FOAIE_DATE) Then
        GenereazaRapoarte = "EROARE: lipseste foaia '" & FOAIE_DATE & "'."
        Exit Function
    End If
    Set ws = ThisWorkbook.Worksheets(FOAIE_DATE)
    date_ = ws.Cells(1, 1).CurrentRegion.Value
    If Not IsArray(date_) Then
        GenereazaRapoarte = "EROARE: foaia '" & FOAIE_DATE & "' nu are date."
        Exit Function
    End If
    colKey = Coloana(date_, camp)
    If colKey = 0 Then
        GenereazaRapoarte = "EROARE: nu gasesc coloana '" & camp & "' in antet."
        Exit Function
    End If
    If Len(campSuma) > 0 Then
        colSuma = Coloana(date_, campSuma)
        If colSuma = 0 Then
            GenereazaRapoarte = "EROARE: nu gasesc coloana '" & campSuma & "' in antet."
            Exit Function
        End If
    End If

    ' Cheile distincte, in ordinea aparitiei.
    Set chei = CreateObject("Scripting.Dictionary")
    chei.CompareMode = vbTextCompare
    For i = 2 To UBound(date_, 1)
        If Len(Trim$(CStr(date_(i, colKey)))) > 0 Then
            If Not chei.Exists(CStr(date_(i, colKey))) Then chei.Add CStr(date_(i, colKey)), chei.Count + 1
        End If
    Next i

    On Error GoTo Eroare
    Application.ScreenUpdating = False
    Application.DisplayAlerts = False
    Application.EnableEvents = False

    If FoaieExista(FOAIE_TEMP) Then ThisWorkbook.Worksheets(FOAIE_TEMP).Delete ' ramasa de la o rulare intrerupta
    ' Valori diferite pot da acelasi nume de fisier ("14/B" si "14_B"): al doilea
    ' primeste un numar, ca sa nu se suprascrie rapoartele intre ele.
    Set folosite = CreateObject("Scripting.Dictionary")
    For Each cheie In chei.Keys
        baza = prefix & "_" & NumeFisierSigur(CStr(cheie))
        nume = baza: k = 1
        Do While folosite.Exists(LCase$(nume))
            k = k + 1
            nume = baza & "_" & k
        Loop
        folosite(LCase$(nume)) = True
        fis = folder & nume & ".pdf"
        If Len(Dir(fis)) > 0 And Not suprascrie Then
            sarite = sarite + 1
        Else
            n = 0
            For i = 2 To UBound(date_, 1)
                If StrComp(CStr(date_(i, colKey)), CStr(cheie), vbTextCompare) = 0 Then n = n + 1
            Next i
            ReDim bloc(1 To n + 1, 1 To UBound(date_, 2))
            For j = 1 To UBound(date_, 2)
                bloc(1, j) = date_(1, j)
            Next j
            r = 1: total = 0
            For i = 2 To UBound(date_, 1)
                If StrComp(CStr(date_(i, colKey)), CStr(cheie), vbTextCompare) = 0 Then
                    r = r + 1
                    For j = 1 To UBound(date_, 2)
                        bloc(r, j) = date_(i, j)
                    Next j
                    If colSuma > 0 Then If IsNumeric(date_(i, colSuma)) Then total = total + CDbl(date_(i, colSuma))
                End If
            Next i

            Set wsT = ThisWorkbook.Worksheets.Add(After:=ThisWorkbook.Worksheets(ThisWorkbook.Worksheets.Count))
            wsT.Name = FOAIE_TEMP
            wsT.Cells(1, 1).Value = RO(TITLU) & CStr(cheie)
            wsT.Cells(1, 1).Font.Bold = True
            wsT.Cells(1, 1).Font.Size = 14
            wsT.Cells(3, 1).Resize(n + 1, UBound(date_, 2)).Value = bloc
            wsT.Rows(3).Font.Bold = True
            If colSuma > 0 Then
                wsT.Cells(n + 4, IIf(colSuma > 1, colSuma - 1, colSuma + 1)).Value = "Total"
                wsT.Cells(n + 4, colSuma).Value = R2(total)
                wsT.Cells(4, colSuma).Resize(n + 1, 1).NumberFormat = "#,##0.00"
                wsT.Rows(n + 4).Font.Bold = True
            End If
            wsT.Columns.AutoFit
            Application.PrintCommunication = False
            With wsT.PageSetup
                .Orientation = xlPortrait
                .Zoom = False
                .FitToPagesWide = 1
                .FitToPagesTall = False
                .CenterFooter = "Pagina &P din &N"
            End With
            Application.PrintCommunication = True
            wsT.ExportAsFixedFormat Type:=xlTypePDF, Filename:=fis, Quality:=xlQualityStandard, _
                                    IncludeDocProperties:=False, IgnorePrintAreas:=False, OpenAfterPublish:=False
            wsT.Delete
            Set wsT = Nothing
            facute = facute + 1
        End If
    Next cheie

    GenereazaRapoarte = "OK: " & facute & " PDF-uri create in " & folder & _
                        IIf(sarite > 0, vbLf & sarite & " existente pastrate (nesuprascrise).", "")
Curatare:
    Application.PrintCommunication = True
    Application.EnableEvents = True
    Application.DisplayAlerts = True
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    GenereazaRapoarte = "EROARE: " & Err.Description & " (la: " & CStr(cheie) & ")"
    On Error Resume Next
    If Not wsT Is Nothing Then wsT.Delete
    On Error GoTo 0
    Resume Curatare
End Function

Private Function Coloana(ByVal date_ As Variant, ByVal nume As String) As Long
    Dim j As Long
    For j = 1 To UBound(date_, 2)
        If StrComp(Trim$(CStr(date_(1, j))), Trim$(nume), vbTextCompare) = 0 Then Coloana = j: Exit Function
    Next j
End Function

Private Function FoaieExista(ByVal nume As String) As Boolean
    Dim ws As Worksheet
    For Each ws In ThisWorkbook.Worksheets
        If StrComp(ws.Name, nume, vbTextCompare) = 0 Then FoaieExista = True: Exit Function
    Next ws
End Function

' Inlocuieste caracterele interzise in numele de fisier Windows si caracterele de
' control (cod 0-31).
Private Function NumeFisierSigur(ByVal s As String) As String
    Dim c As Variant, k As Long
    For k = 0 To 31
        s = Replace(s, Chr(k), "_")
    Next k
    For Each c In Array("\", "/", ":", "*", "?", """", "<", ">", "|")
        s = Replace(s, c, "_")
    Next c
    s = Trim$(s)
    If Len(s) = 0 Then s = "fara_nume"
    NumeFisierSigur = Left$(s, 100)
End Function

' Rotunjire la 2 zecimale "de la jumatate in sus" (ca ROUND din Excel). Round() din
' VBA rotunjeste la par (0,125 -> 0,12), gresit pentru sume de plata.
Private Function R2(ByVal x As Double) As Double
    R2 = Application.WorksheetFunction.Round(x, 2)
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
