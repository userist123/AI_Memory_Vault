Attribute VB_Name = "Excel_CurataDate"
' =============================================================================
' Modul:    Excel_CurataDate
' Scop:     Curata si valideaza un tabel: spatii in plus si spatii nedespartibile,
'           sume scrise ca text ("1.234,56" -> 1234,56), date scrise ca text
'           ("05.03.2026" -> data), randuri duplicate; marcheaza in coloana
'           "Erori" valorile care nu pot fi convertite.
' Rulare:   Alt+F8 > CurataDate. Tabelul este in foaia "Date", antet pe randul 1.
' Modifica: creeaza foaia "Date_curate" (cere confirmare daca exista). Foaia
'           "Date" ramane neschimbata, ca sa puteti compara.
' Versiune: 1.0
' =============================================================================
Option Explicit

Private Const FOAIE_SURSA As String = "Date"
Private Const FOAIE_REZ As String = "Date_curate"
Private Const COL_ERORI As String = "Erori"

Public Sub CurataDate()
    Dim numerice As String, dateCal As String, rez As String, inloc As Boolean
    On Error GoTo Eroare
    numerice = InputBox("Coloanele cu sume (separate prin virgula):", "Curatare date", "Suma")
    dateCal = InputBox("Coloanele cu date calendaristice (separate prin virgula):", "Curatare date", "Data")
    If FoaieExista(FOAIE_REZ) Then
        If MsgBox("Foaia '" & FOAIE_REZ & "' exista. O inlocuiesc?", vbYesNo + vbQuestion, "Curatare date") <> vbYes Then Exit Sub
        inloc = True
    End If
    rez = CurataTabel(numerice, dateCal, True, inloc)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Curatare date"
    Exit Sub
Eroare:
    MsgBox "Curatarea nu a reusit: " & Err.Description, vbCritical, "Curatare date"
End Sub

Public Function CurataTabel(ByVal coloaneSume As String, ByVal coloaneDate As String, _
                            ByVal eliminaDuplicate As Boolean, ByVal inlocuieste As Boolean) As String
    Dim d As Variant, rez() As Variant, nR As Long, nC As Long, i As Long, j As Long, r As Long
    Dim tip() As Long, v As Variant, err_ As String, cheie As String, vazute As Object
    Dim dup As Long, gresite As Long, ws As Worksheet, numar As Double, data_ As Date, nume As Variant

    If Not FoaieExista(FOAIE_SURSA) Then
        CurataTabel = "EROARE: lipseste foaia '" & FOAIE_SURSA & "'."
        Exit Function
    End If
    If FoaieExista(FOAIE_REZ) And Not inlocuieste Then
        CurataTabel = "EROARE: foaia '" & FOAIE_REZ & "' exista; nu o inlocuiesc fara confirmare."
        Exit Function
    End If
    d = ThisWorkbook.Worksheets(FOAIE_SURSA).Cells(1, 1).CurrentRegion.Value
    If Not IsArray(d) Then
        CurataTabel = "EROARE: foaia '" & FOAIE_SURSA & "' nu are date."
        Exit Function
    End If
    nR = UBound(d, 1): nC = UBound(d, 2)

    ' tip coloana: 0 text, 1 suma, 2 data
    ReDim tip(1 To nC)
    For Each nume In Split(coloaneSume, ",")
        j = Coloana(d, CStr(nume))
        If Len(Trim$(nume)) > 0 And j = 0 Then CurataTabel = "EROARE: coloana '" & Trim$(nume) & "' nu exista.": Exit Function
        If j > 0 Then tip(j) = 1
    Next nume
    For Each nume In Split(coloaneDate, ",")
        j = Coloana(d, CStr(nume))
        If Len(Trim$(nume)) > 0 And j = 0 Then CurataTabel = "EROARE: coloana '" & Trim$(nume) & "' nu exista.": Exit Function
        If j > 0 Then tip(j) = 2
    Next nume

    On Error GoTo Eroare
    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.DisplayAlerts = False

    Set vazute = CreateObject("Scripting.Dictionary")
    ReDim rez(1 To nR, 1 To nC + 1)
    For j = 1 To nC
        rez(1, j) = CurataText(CStr(d(1, j)))
    Next j
    rez(1, nC + 1) = COL_ERORI
    r = 1
    For i = 2 To nR
        err_ = "": cheie = ""
        For j = 1 To nC
            v = d(i, j)
            If VarType(v) = vbString Then v = CurataText(CStr(v))
            If tip(j) = 1 And VarType(v) = vbString And Len(v) > 0 Then
                If TextInNumar(CStr(v), numar) Then v = numar Else err_ = err_ & d(1, j) & " nu e suma; "
            ElseIf tip(j) = 2 And VarType(v) = vbString And Len(v) > 0 Then
                If TextInData(CStr(v), data_) Then v = data_ Else err_ = err_ & d(1, j) & " nu e data; "
            End If
            d(i, j) = v
            cheie = cheie & "|" & CStr(v)
        Next j
        If eliminaDuplicate And vazute.Exists(cheie) Then
            dup = dup + 1
        Else
            vazute(cheie) = True
            r = r + 1
            For j = 1 To nC
                rez(r, j) = d(i, j)
            Next j
            rez(r, nC + 1) = err_
            If Len(err_) > 0 Then gresite = gresite + 1
        End If
    Next i

    If FoaieExista(FOAIE_REZ) Then ThisWorkbook.Worksheets(FOAIE_REZ).Delete
    Set ws = ThisWorkbook.Worksheets.Add(After:=ThisWorkbook.Worksheets(ThisWorkbook.Worksheets.Count))
    ws.Name = FOAIE_REZ
    ws.Cells(1, 1).Resize(r, nC + 1).Value = rez
    For j = 1 To nC
        If tip(j) = 1 Then ws.Cells(2, j).Resize(r, 1).NumberFormat = "#,##0.00"
        If tip(j) = 2 Then ws.Cells(2, j).Resize(r, 1).NumberFormat = "dd.mm.yyyy"
    Next j
    ws.Rows(1).Font.Bold = True
    ws.Columns.AutoFit

    CurataTabel = "OK: " & (r - 1) & " randuri pastrate, " & dup & " duplicate eliminate, " & _
                  gresite & " randuri cu erori marcate in coloana '" & COL_ERORI & "'."
Curatare:
    Application.DisplayAlerts = True
    Application.EnableEvents = True
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    CurataTabel = "EROARE: " & Err.Description & " (randul " & i & ")"
    Resume Curatare
End Function

' Spatii nedespartibile, tab-uri, spatii duble, spatii la capete.
Private Function CurataText(ByVal s As String) As String
    s = Replace(s, ChrW(160), " ")
    s = Replace(s, vbTab, " ")
    s = Replace(s, vbCr, " ")
    s = Replace(s, vbLf, " ")
    Do While InStr(s, "  ") > 0
        s = Replace(s, "  ", " ")
    Loop
    CurataText = Trim$(s)
End Function

' "1.234,56" / "1234,56" / "1234.56" / "1 234,56 lei" -> numar. Fals daca nu e numar.
Private Function TextInNumar(ByVal s As String, ByRef numar As Double) As Boolean
    Dim t As String
    t = Replace(Replace(Replace(LCase$(s), "lei", ""), "ron", ""), " ", "")
    If Len(t) = 0 Then Exit Function
    If InStr(t, ",") > 0 Then
        t = Replace(t, ".", "")
        t = Replace(t, ",", ".")
    ElseIf Len(t) - Len(Replace(t, ".", "")) > 1 Then
        t = Replace(t, ".", "")
    End If
    If t Like "*[!0-9.+-]*" Or t Like "*.*.*" Then Exit Function
    If Not t Like "*[0-9]*" Or Len(t) - Len(Replace(Replace(t, "+", ""), "-", "")) > 1 Then Exit Function
    numar = Val(t) ' Val foloseste mereu punctul zecimal, indiferent de setarile regionale
    TextInNumar = True
End Function

' "05.03.2026", "5/3/2026", "2026-03-05" -> data (zi-luna-an pentru formele cu punct sau bara).
Private Function TextInData(ByVal s As String, ByRef d As Date) As Boolean
    Dim p() As String, z As Long, l As Long, a As Long
    s = Replace(Replace(s, "/", "."), "-", ".")
    p = Split(s, ".")
    If UBound(p) <> 2 Then Exit Function
    If Not (IsNumeric(p(0)) And IsNumeric(p(1)) And IsNumeric(p(2))) Then Exit Function
    If Len(p(0)) = 4 Then
        a = CLng(p(0)): l = CLng(p(1)): z = CLng(p(2))
    Else
        z = CLng(p(0)): l = CLng(p(1)): a = CLng(p(2))
        If a < 100 Then a = a + 2000
    End If
    If l < 1 Or l > 12 Or z < 1 Or z > 31 Or a < 1900 Or a > 2100 Then Exit Function
    d = DateSerial(a, l, z)
    If Day(d) <> z Then Exit Function ' 31.02 nu exista
    TextInData = True
End Function

Private Function Coloana(ByVal d As Variant, ByVal nume As String) As Long
    Dim j As Long
    For j = 1 To UBound(d, 2)
        If StrComp(CurataText(CStr(d(1, j))), Trim$(nume), vbTextCompare) = 0 Then Coloana = j: Exit Function
    Next j
End Function

Private Function FoaieExista(ByVal nume As String) As Boolean
    Dim ws As Worksheet
    For Each ws In ThisWorkbook.Worksheets
        If StrComp(ws.Name, nume, vbTextCompare) = 0 Then FoaieExista = True: Exit Function
    Next ws
End Function
