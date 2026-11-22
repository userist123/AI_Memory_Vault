Attribute VB_Name = "Word_ActualizeazaCampuri"
' =============================================================================
' Modul:    Word_ActualizeazaCampuri
' Scop:     Actualizeaza toate campurile dintr-un document: cuprins, liste de
'           tabele/figuri, numerotari SEQ, trimiteri REF/PAGEREF, campuri din
'           antet, subsol si casete de text; raporteaza campurile cu eroare
'           ("Error!" / "Eroare!").
' Rulare:   Alt+F8 > ActualizeazaCampuri (documentul deschis) sau
'           ActualizeazaFolder (toate documentele dintr-un folder, in copii).
' Modifica: documentul deschis (nu il salveaza - salvati dumneavoastra dupa ce
'           verificati); in varianta pe folder, doar copiile din folderul de
'           rezultat, cu confirmare pentru suprascriere.
' Versiune: 1.0
' =============================================================================
Option Explicit

Public Sub ActualizeazaCampuri()
    Dim rez As String
    On Error GoTo Eroare
    If Documents.Count = 0 Then MsgBox "Nu este deschis niciun document.", vbExclamation, "Campuri": Exit Sub
    rez = ActualizeazaDocument(ActiveDocument)
    MsgBox rez, IIf(InStr(rez, "cu eroare") > 0, vbExclamation, vbInformation), "Campuri"
    Exit Sub
Eroare:
    MsgBox "Actualizarea nu a reusit: " & Err.Description, vbCritical, "Campuri"
End Sub

Public Sub ActualizeazaFolder()
    Dim sursa As String, dest As String, rasp As VbMsgBoxResult, rez As String
    On Error GoTo Eroare
    sursa = AlegeFolder("Folderul cu documentele")
    If Len(sursa) = 0 Then Exit Sub
    dest = AlegeFolder("Folderul in care salvez copiile actualizate")
    If Len(dest) = 0 Then Exit Sub
    rasp = MsgBox("Suprascriu copiile existente?" & vbLf & "Nu = le sar.", vbYesNoCancel + vbQuestion, "Campuri")
    If rasp = vbCancel Then Exit Sub
    rez = ActualizeazaFisiere(sursa, dest, rasp = vbYes)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Campuri"
    Exit Sub
Eroare:
    MsgBox "Actualizarea nu a reusit: " & Err.Description, vbCritical, "Campuri"
End Sub

Public Function ActualizeazaFisiere(ByVal sursa As String, ByVal dest As String, ByVal suprascrie As Boolean) As String
    Dim v As Variant, fis As String, doc As Document, alerte As Long, secAuto As Long, nFis As Long, sarite As Long
    Dim jurnal As String

    If Right$(sursa, 1) <> "\" Then sursa = sursa & "\"
    If Right$(dest, 1) <> "\" Then dest = dest & "\"
    If Len(Dir(sursa, vbDirectory)) = 0 Or Len(Dir(dest, vbDirectory)) = 0 Then
        ActualizeazaFisiere = "EROARE: folderul sursa sau cel de rezultat nu exista."
        Exit Function
    End If
    If StrComp(sursa, dest, vbTextCompare) = 0 Then
        ActualizeazaFisiere = "EROARE: folderul de rezultat trebuie sa fie altul decat sursa."
        Exit Function
    End If

    alerte = Application.DisplayAlerts
    secAuto = Application.AutomationSecurity
    On Error GoTo Eroare
    Application.ScreenUpdating = False
    Application.DisplayAlerts = wdAlertsNone
    Application.AutomationSecurity = msoAutomationSecurityForceDisable

    For Each v In ListaFisiere(sursa, "*.doc*")
        fis = CStr(v)
        If Left$(fis, 2) <> "~$" Then
            If Len(Dir(dest & fis)) > 0 And Not suprascrie Then
                sarite = sarite + 1
            Else
                Set doc = Documents.Open(FileName:=sursa & fis, ReadOnly:=True, AddToRecentFiles:=False, Visible:=False)
                jurnal = jurnal & vbLf & fis & ": " & ActualizeazaDocument(doc)
                doc.SaveAs2 FileName:=dest & fis, AddToRecentFiles:=False
                doc.Close SaveChanges:=wdDoNotSaveChanges
                Set doc = Nothing
                nFis = nFis + 1
            End If
        End If
    Next v
    ActualizeazaFisiere = "OK: " & nFis & " documente actualizate." & _
        IIf(sarite > 0, vbLf & sarite & " copii existente pastrate.", "") & jurnal
Curatare:
    Application.AutomationSecurity = secAuto
    Application.DisplayAlerts = alerte
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    ActualizeazaFisiere = "EROARE: " & Err.Description & " (document: " & fis & ")"
    On Error Resume Next
    If Not doc Is Nothing Then doc.Close SaveChanges:=wdDoNotSaveChanges
    On Error GoTo 0
    Resume Curatare
End Function

' Actualizeaza totul de doua ori (al doilea tur prinde numerele de pagina mutate
' de cuprinsul nou). Intoarce un rezumat.
Public Function ActualizeazaDocument(ByVal doc As Document) As String
    Dim story As Range, tur As Long, t As TableOfContents, f As TableOfFigures, n As Long, gresite As Long
    Dim fld As Field
    On Error GoTo Eroare
    For tur = 1 To 2
        For Each story In doc.StoryRanges
            Do
                story.Fields.Update
                Set story = story.NextStoryRange
            Loop Until story Is Nothing
        Next story
        For Each t In doc.TablesOfContents
            t.Update
        Next t
        For Each f In doc.TablesOfFigures
            f.Update
        Next f
    Next tur
    For Each story In doc.StoryRanges
        Do
            For Each fld In story.Fields
                n = n + 1
                If InStr(1, fld.Result.Text, "Error!", vbTextCompare) > 0 Or _
                   InStr(1, fld.Result.Text, "Eroare!", vbTextCompare) > 0 Then gresite = gresite + 1
            Next fld
            Set story = story.NextStoryRange
        Loop Until story Is Nothing
    Next story
    ActualizeazaDocument = n & " campuri, " & doc.TablesOfContents.Count & " cuprins(uri)" & _
        IIf(gresite > 0, ", " & gresite & " cu eroare (verificati marcajele/trimiterile)", ", fara erori")
    Exit Function
Eroare:
    ActualizeazaDocument = "EROARE la actualizare: " & Err.Description
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
