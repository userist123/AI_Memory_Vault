Attribute VB_Name = "Word_InlocuireLot"
' =============================================================================
' Modul:    Word_InlocuireLot
' Scop:     Cauta si inlocuieste o lista de texte in toate documentele Word dintr-un
'           folder, inclusiv in antet, subsol, note si casete de text; optional
'           pune textul nou cu aldine sau evidentiat. Numara inlocuirile.
' Rulare:   Puneti in documentul cu macroul un tabel cu 2 coloane (Text vechi |
'           Text nou, primul rand = antet). Alt+F8 > InlocuireLot > alegeti
'           folderul cu documente si folderul de rezultat.
' Modifica: NU modifica originalele: salveaza copii in folderul de rezultat
'           (fisierele existente se sar, daca nu confirmati suprascrierea).
' Versiune: 1.1
' =============================================================================
Option Explicit

Private Const SEP_PERECHE As String = "||"
Private Const SEP_VALOARE As String = "=>"

Public Sub InlocuireLot()
    Dim perechi As String, sursa As String, dest As String, rasp As VbMsgBoxResult, rez As String
    Dim t As Table, i As Long, vechi As String, nou As String
    On Error GoTo Eroare
    If ThisDocument.Tables.Count = 0 Then
        MsgBox "Puneti in acest document un tabel cu 2 coloane: Text vechi | Text nou.", vbExclamation, "Inlocuire"
        Exit Sub
    End If
    Set t = ThisDocument.Tables(1)
    For i = 2 To t.Rows.Count
        vechi = TextCelula(t.Cell(i, 1).Range.Text)
        nou = TextCelula(t.Cell(i, 2).Range.Text)
        If Len(vechi) > 0 Then perechi = perechi & IIf(Len(perechi) > 0, SEP_PERECHE, "") & vechi & SEP_VALOARE & nou
    Next i
    If Len(perechi) = 0 Then MsgBox "Tabelul nu are randuri de inlocuit.", vbExclamation, "Inlocuire": Exit Sub
    sursa = AlegeFolder("Folderul cu documentele de modificat")
    If Len(sursa) = 0 Then Exit Sub
    dest = AlegeFolder("Folderul in care salvez copiile modificate")
    If Len(dest) = 0 Then Exit Sub
    rasp = MsgBox("Suprascriu copiile existente in folderul de rezultat?" & vbLf & "Nu = le sar.", _
                  vbYesNoCancel + vbQuestion, "Inlocuire")
    If rasp = vbCancel Then Exit Sub
    rez = InlocuiesteInFolder(sursa, dest, perechi, True, "", rasp = vbYes)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Inlocuire"
    Exit Sub
Eroare:
    MsgBox "Inlocuirea nu a reusit: " & Err.Description, vbCritical, "Inlocuire"
End Sub

' perechi: "vechi1=>nou1||vechi2=>nou2". formatNou: "" | "aldine" | "evidentiat".
Public Function InlocuiesteInFolder(ByVal sursa As String, ByVal dest As String, ByVal perechi As String, _
                                    ByVal potrivireMajuscule As Boolean, ByVal formatNou As String, _
                                    ByVal suprascrie As Boolean) As String
    Dim v As Variant, fis As String, doc As Document, lista() As String, p() As String, k As Long
    Dim alerte As Long, secAuto As Long, nFis As Long, nTot As Long, nDoc As Long, sarite As Long
    Dim jurnal As String, story As Range, deschise As String, linkuri As Boolean

    If Right$(sursa, 1) <> "\" Then sursa = sursa & "\"
    If Right$(dest, 1) <> "\" Then dest = dest & "\"
    If Len(Dir(sursa, vbDirectory)) = 0 Or Len(Dir(dest, vbDirectory)) = 0 Then
        InlocuiesteInFolder = "EROARE: folderul sursa sau cel de rezultat nu exista."
        Exit Function
    End If
    If StrComp(sursa, dest, vbTextCompare) = 0 Then
        InlocuiesteInFolder = "EROARE: folderul de rezultat trebuie sa fie altul decat sursa (originalele raman neatinse)."
        Exit Function
    End If
    lista = Split(perechi, SEP_PERECHE)
    For k = 0 To UBound(lista)
        If InStr(lista(k), SEP_VALOARE) = 0 Then
            InlocuiesteInFolder = "EROARE: pereche fara '" & SEP_VALOARE & "': " & lista(k)
            Exit Function
        End If
    Next k

    alerte = Application.DisplayAlerts
    secAuto = Application.AutomationSecurity
    linkuri = Options.UpdateLinksAtOpen
    On Error GoTo Eroare
    Application.ScreenUpdating = False
    Application.DisplayAlerts = wdAlertsNone
    Application.AutomationSecurity = msoAutomationSecurityForceDisable
    Options.UpdateLinksAtOpen = False ' legaturile LINK din documentele straine nu se reimprospateaza

    For Each v In ListaFisiere(sursa, "*.doc*")
        fis = CStr(v)
        If Left$(fis, 2) <> "~$" Then
            If DejaDeschis(sursa & fis) Or DejaDeschis(dest & fis) Then
                deschise = deschise & vbLf & "  " & fis
            ElseIf Len(Dir(dest & fis)) > 0 And Not suprascrie Then
                sarite = sarite + 1
            Else
                Set doc = Documents.Open(FileName:=sursa & fis, ReadOnly:=True, AddToRecentFiles:=False, Visible:=False)
                nDoc = 0
                For k = 0 To UBound(lista)
                    p = Split(lista(k), SEP_VALOARE)
                    For Each story In doc.StoryRanges
                        Do
                            nDoc = nDoc + InlocuiesteInZona(story, p(0), p(1), potrivireMajuscule, formatNou)
                            Set story = story.NextStoryRange
                        Loop Until story Is Nothing
                    Next story
                Next k
                doc.SaveAs2 FileName:=dest & fis, AddToRecentFiles:=False
                doc.Close SaveChanges:=wdDoNotSaveChanges
                Set doc = Nothing
                nFis = nFis + 1
                nTot = nTot + nDoc
                jurnal = jurnal & vbLf & fis & ": " & nDoc
            End If
        End If
    Next v

    InlocuiesteInFolder = "OK: " & nTot & " inlocuiri in " & nFis & " documente." & _
        IIf(sarite > 0, vbLf & sarite & " copii existente pastrate.", "") & _
        IIf(Len(deschise) > 0, vbLf & "Sarite (deschise acum in Word, inclusiv documentul cu macroul):" & deschise, "") & jurnal
Curatare:
    Options.UpdateLinksAtOpen = linkuri
    Application.AutomationSecurity = secAuto
    Application.DisplayAlerts = alerte
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    InlocuiesteInFolder = "EROARE: " & Err.Description & " (document: " & fis & ")"
    On Error Resume Next
    If Not doc Is Nothing Then doc.Close SaveChanges:=wdDoNotSaveChanges
    On Error GoTo 0
    Resume Curatare
End Function

' Inlocuieste una cate una (ca sa le numere) intr-o zona; intoarce numarul.
Private Function InlocuiesteInZona(ByVal zona As Range, ByVal vechi As String, ByVal nou As String, _
                                   ByVal majuscule As Boolean, ByVal formatNou As String) As Long
    Dim r As Range, n As Long
    Set r = zona.Duplicate
    With r.Find
        .ClearFormatting
        .Replacement.ClearFormatting
        .Text = vechi
        .Replacement.Text = nou
        .MatchCase = majuscule
        .MatchWholeWord = False
        .MatchWildcards = False
        .Forward = True
        .Wrap = wdFindStop
        .Format = Len(formatNou) > 0
        If formatNou = "aldine" Then .Replacement.Font.Bold = True
        If formatNou = "evidentiat" Then .Replacement.Highlight = True
    End With
    Do While r.Find.Execute(Replace:=wdReplaceOne)
        n = n + 1
        r.Collapse wdCollapseEnd
        If n > 100000 Then Exit Do ' siguranta impotriva buclei infinite
    Loop
    InlocuiesteInZona = n
End Function

Private Function TextCelula(ByVal s As String) As String
    TextCelula = Trim$(Replace(Replace(s, Chr(13), ""), Chr(7), ""))
End Function

Private Function AlegeFolder(ByVal titlu As String) As String
    With Application.FileDialog(msoFileDialogFolderPicker)
        .Title = titlu
        If .Show = -1 Then AlegeFolder = .SelectedItems(1)
    End With
End Function

' Lista fisierelor, citita complet inainte de prelucrare: un Dir() apelat in
' bucla (ex. verificarea fisierului existent) ar reseta enumerarea.
Private Function ListaFisiere(ByVal folder As String, ByVal masca As String) As Collection
    Dim c As New Collection, f As String
    f = Dir(folder & masca)
    Do While Len(f) > 0
        If Left$(f, 2) <> "~$" Then c.Add f
        f = Dir()
    Loop
    Set ListaFisiere = c
End Function

' Documentul cu macroul si orice document deschis acum nu se redeschid si nu se
' inchid (s-ar inchide documentul utilizatorului): se sar si se raporteaza.
Private Function DejaDeschis(ByVal cale As String) As Boolean
    Dim d As Document
    If StrComp(cale, ThisDocument.FullName, vbTextCompare) = 0 Then DejaDeschis = True: Exit Function
    For Each d In Documents
        If StrComp(cale, d.FullName, vbTextCompare) = 0 Then DejaDeschis = True: Exit Function
    Next d
End Function
