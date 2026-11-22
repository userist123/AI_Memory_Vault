Attribute VB_Name = "Excel_Restante"
' =============================================================================
' Modul:    Excel_Restante
' Scop:     Calculeaza zilele de intarziere si penalitatile pentru restante,
'           cu parametrii dintr-o foaie de intrare ("Parametri"): data calculului,
'           cota de penalizare pe zi (%) si plafonul (% din suma datorata).
'           Cota si plafonul se verifica de utilizator (contract, regulament, lege).
' Rulare:   Alt+F8 > CalculRestante. La prima rulare creeaza foaia "Parametri"
'           cu valori de pornire; completati-o si rulati din nou.
'           Datele: foaia "Restante", coloane Apartament, Titular, Suma, Scadenta.
' Modifica: creeaza/inlocuieste foaia "Calcul" (cu confirmare). Foaia "Restante"
'           nu se modifica.
' Versiune: 1.1
' =============================================================================
Option Explicit

Private Const FOAIE_DATE As String = "Restante"
Private Const FOAIE_PARAM As String = "Parametri"
Private Const FOAIE_REZ As String = "Calcul"
Private Const N_DATA As String = "DataCalcul"
Private Const N_COTA As String = "CotaPenalizareZi"
Private Const N_PLAFON As String = "PlafonProcent"
Private Const H_SUMA As String = "Suma"
Private Const H_SCADENTA As String = "Scadenta"

Public Sub CalculRestante()
    Dim rez As String, inloc As Boolean
    On Error GoTo Eroare
    If Not NumeExista(N_DATA) Or Not NumeExista(N_COTA) Or Not NumeExista(N_PLAFON) Then
        CreeazaParametri
        MsgBox "Am creat foaia '" & FOAIE_PARAM & "'. Verificati data, cota si plafonul, apoi rulati din nou.", _
               vbInformation, "Restante"
        Exit Sub
    End If
    If FoaieExista(FOAIE_REZ) Then
        If MsgBox("Foaia '" & FOAIE_REZ & "' exista. O recalculez (se inlocuieste)?", vbYesNo + vbQuestion, _
                  "Restante") <> vbYes Then Exit Sub
        inloc = True
    End If
    rez = CalculeazaPenalitati(inloc)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Restante"
    Exit Sub
Eroare:
    MsgBox "Calculul nu a reusit: " & Err.Description, vbCritical, "Restante"
End Sub

Public Function CalculeazaPenalitati(ByVal inlocuieste As Boolean) As String
    Dim d As Variant, out() As Variant, i As Long, j As Long, nR As Long, nC As Long
    Dim cSuma As Long, cScad As Long, dataCalc As Date, cota As Double, plafon As Double
    Dim zile As Long, pen As Double, suma As Double, totSuma As Double, totPen As Double
    Dim gresite As String, ws As Worksheet

    ' --- validarea intrarilor, inainte de orice modificare
    If Not NumeExista(N_DATA) Or Not NumeExista(N_COTA) Or Not NumeExista(N_PLAFON) Then _
        CalculeazaPenalitati = "EROARE: lipsesc parametrii (rulati CalculRestante o data).": Exit Function
    If Not FoaieExista(FOAIE_DATE) Then CalculeazaPenalitati = "EROARE: lipseste foaia '" & FOAIE_DATE & "'.": Exit Function
    If Not IsDate(ThisWorkbook.Names(N_DATA).RefersToRange.Value) Then _
        CalculeazaPenalitati = "EROARE: " & N_DATA & " nu este o data.": Exit Function
    dataCalc = Int(CDate(ThisWorkbook.Names(N_DATA).RefersToRange.Value)) ' fara ora
    If Not IsNumeric(ThisWorkbook.Names(N_COTA).RefersToRange.Value2) Or _
       Not IsNumeric(ThisWorkbook.Names(N_PLAFON).RefersToRange.Value2) Then _
        CalculeazaPenalitati = "EROARE: cota si plafonul trebuie sa fie numere.": Exit Function
    cota = CDbl(ThisWorkbook.Names(N_COTA).RefersToRange.Value2)
    plafon = CDbl(ThisWorkbook.Names(N_PLAFON).RefersToRange.Value2)
    If cota <= 0 Or cota > 1 Then CalculeazaPenalitati = "EROARE: cota pe zi trebuie sa fie intre 0 si 1 (%).": Exit Function
    If plafon <= 0 Or plafon > 100 Then CalculeazaPenalitati = "EROARE: plafonul trebuie sa fie intre 0 si 100 (%).": Exit Function
    If FoaieExista(FOAIE_REZ) And Not inlocuieste Then
        CalculeazaPenalitati = "EROARE: foaia '" & FOAIE_REZ & "' exista; nu o inlocuiesc fara confirmare."
        Exit Function
    End If
    d = ThisWorkbook.Worksheets(FOAIE_DATE).Cells(1, 1).CurrentRegion.Value
    If Not IsArray(d) Then CalculeazaPenalitati = "EROARE: foaia '" & FOAIE_DATE & "' nu are date.": Exit Function
    nR = UBound(d, 1): nC = UBound(d, 2)
    cSuma = Coloana(d, H_SUMA): cScad = Coloana(d, H_SCADENTA)
    If cSuma = 0 Or cScad = 0 Then
        CalculeazaPenalitati = "EROARE: lipsesc coloanele '" & H_SUMA & "' / '" & H_SCADENTA & "'."
        Exit Function
    End If

    On Error GoTo Eroare
    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.DisplayAlerts = False

    ReDim out(1 To nR + 1, 1 To nC + 3)
    For j = 1 To nC
        out(1, j) = d(1, j)
    Next j
    out(1, nC + 1) = RO("Zile {i^}nt{a^}rziere")
    out(1, nC + 2) = "Penalitate"
    out(1, nC + 3) = RO("Total de plat{a~}")
    For i = 2 To nR
        For j = 1 To nC
            out(i, j) = d(i, j)
        Next j
        If Not IsNumeric(d(i, cSuma)) Or IsEmpty(d(i, cSuma)) Or Not IsDate(d(i, cScad)) Then
            gresite = gresite & " " & i
            out(i, nC + 1) = "date invalide"
        ElseIf CDbl(d(i, cSuma)) < 0 Then
            gresite = gresite & " " & i
            out(i, nC + 1) = "suma negativa"
        Else
            suma = CDbl(d(i, cSuma))
            zile = dataCalc - Int(CDate(d(i, cScad)))
            If zile < 0 Then zile = 0
            pen = R2(suma * cota / 100 * zile)
            If pen > R2(suma * plafon / 100) Then pen = R2(suma * plafon / 100)
            out(i, nC + 1) = zile
            out(i, nC + 2) = pen
            out(i, nC + 3) = R2(suma + pen)
            totSuma = totSuma + suma
            totPen = totPen + pen
        End If
    Next i
    out(nR + 1, 1) = "TOTAL"
    out(nR + 1, cSuma) = R2(totSuma)
    out(nR + 1, nC + 2) = R2(totPen)
    out(nR + 1, nC + 3) = R2(totSuma + totPen)

    If FoaieExista(FOAIE_REZ) Then ThisWorkbook.Worksheets(FOAIE_REZ).Delete
    Set ws = ThisWorkbook.Worksheets.Add(After:=ThisWorkbook.Worksheets(ThisWorkbook.Worksheets.Count))
    ws.Name = FOAIE_REZ
    ws.Cells(1, 1).Resize(nR + 1, nC + 3).Value = out
    ws.Rows(1).Font.Bold = True
    ws.Rows(nR + 1).Font.Bold = True
    ws.Cells(2, cSuma).Resize(nR, 1).NumberFormat = "#,##0.00"
    ws.Cells(2, nC + 2).Resize(nR, 2).NumberFormat = "#,##0.00"
    ws.Cells(2, cScad).Resize(nR - 1, 1).NumberFormat = "dd.mm.yyyy"
    ws.Columns.AutoFit

    CalculeazaPenalitati = "OK: " & (nR - 1) & " randuri; penalitati " & Format$(totPen, "#,##0.00") & _
        " la data " & Format$(dataCalc, "dd.mm.yyyy") & "." & _
        IIf(Len(gresite) > 0, vbLf & "Randuri cu date invalide (necalculate):" & gresite, "")
Curatare:
    Application.DisplayAlerts = True
    Application.EnableEvents = True
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    CalculeazaPenalitati = "EROARE: " & Err.Description & " (randul " & i & ")"
    Resume Curatare
End Function

' Foaia de intrare cu nume definite (valorile se verifica de utilizator).
Private Sub CreeazaParametri()
    Dim ws As Worksheet
    If FoaieExista(FOAIE_PARAM) Then
        Set ws = ThisWorkbook.Worksheets(FOAIE_PARAM)
    Else
        Set ws = ThisWorkbook.Worksheets.Add(Before:=ThisWorkbook.Worksheets(1))
        ws.Name = FOAIE_PARAM
    End If
    ws.Cells(1, 1).Value = "Data calculului": ws.Cells(1, 2).Value = Date
    ws.Cells(2, 1).Value = "Cota penalizare pe zi (%)": ws.Cells(2, 2).Value = 0.2
    ws.Cells(3, 1).Value = RO("Plafon (% din sum{a~})"): ws.Cells(3, 2).Value = 100
    ws.Cells(1, 2).NumberFormat = "dd.mm.yyyy"
    ThisWorkbook.Names.Add Name:=N_DATA, RefersTo:="='" & FOAIE_PARAM & "'!$B$1"
    ThisWorkbook.Names.Add Name:=N_COTA, RefersTo:="='" & FOAIE_PARAM & "'!$B$2"
    ThisWorkbook.Names.Add Name:=N_PLAFON, RefersTo:="='" & FOAIE_PARAM & "'!$B$3"
    ws.Columns(1).AutoFit
End Sub

Private Function NumeExista(ByVal nume As String) As Boolean
    Dim n As Name
    For Each n In ThisWorkbook.Names
        If StrComp(n.Name, nume, vbTextCompare) = 0 Then NumeExista = True: Exit Function
    Next n
End Function

Private Function Coloana(ByVal d As Variant, ByVal nume As String) As Long
    Dim j As Long
    For j = 1 To UBound(d, 2)
        If StrComp(Trim$(CStr(d(1, j))), nume, vbTextCompare) = 0 Then Coloana = j: Exit Function
    Next j
End Function

Private Function FoaieExista(ByVal nume As String) As Boolean
    Dim ws As Worksheet
    For Each ws In ThisWorkbook.Worksheets
        If StrComp(ws.Name, nume, vbTextCompare) = 0 Then FoaieExista = True: Exit Function
    Next ws
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
