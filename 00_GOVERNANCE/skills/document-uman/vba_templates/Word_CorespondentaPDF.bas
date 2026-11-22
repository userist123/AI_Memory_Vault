Attribute VB_Name = "Word_CorespondentaPDF"
' =============================================================================
' Modul:    Word_CorespondentaPDF
' Scop:     Imbinare de corespondenta (mail merge) cu un PDF separat pentru fiecare
'           rand din sursa de date, numit dupa o coloana (ex. NumeFisier,
'           Apartament): Notificare_Ap12.pdf, Notificare_Ap13.pdf ...
' Rulare:   Deschideti documentul principal (cu campurile de imbinare si sursa de
'           date atasata din Corespondenta > Selectare destinatari).
'           Alt+F8 > CorespondentaPDF > numele coloanei > folderul de rezultat.
' Modifica: creeaza PDF-uri in folderul ales (existentele se sar, daca nu
'           confirmati suprascrierea). Documentul principal nu se modifica.
' Versiune: 1.1
' =============================================================================
Option Explicit

Private Const PREFIX_IMPLICIT As String = "Document"

Public Sub CorespondentaPDF()
    Dim camp As String, dest As String, rasp As VbMsgBoxResult, rez As String
    On Error GoTo Eroare
    If Documents.Count = 0 Then MsgBox "Deschideti documentul principal.", vbExclamation, "Corespondenta": Exit Sub
    If ActiveDocument.MailMerge.State <> wdMainAndDataSource Then
        MsgBox "Documentul activ nu are o sursa de date atasata (Corespondenta > Selectare destinatari).", _
               vbExclamation, "Corespondenta"
        Exit Sub
    End If
    camp = InputBox("Coloana din care se ia numele fiecarui PDF:", "Corespondenta", "NumeFisier")
    If Len(camp) = 0 Then Exit Sub
    With Application.FileDialog(msoFileDialogFolderPicker)
        .Title = "Folderul in care salvez PDF-urile"
        If .Show <> -1 Then Exit Sub
        dest = .SelectedItems(1)
    End With
    rasp = MsgBox("Suprascriu PDF-urile existente?" & vbLf & "Nu = le sar.", vbYesNoCancel + vbQuestion, "Corespondenta")
    If rasp = vbCancel Then Exit Sub
    rez = ImbinaInPDF(ActiveDocument, camp, dest, PREFIX_IMPLICIT, rasp = vbYes)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Corespondenta"
    Exit Sub
Eroare:
    MsgBox "Imbinarea nu a reusit: " & Err.Description, vbCritical, "Corespondenta"
End Sub

' Varianta pe fisiere (pentru rulare automata/test): deschide documentul principal,
' ataseaza foaia din registrul Excel ca sursa si genereaza PDF-urile.
Public Function ImbinaFisiere(ByVal principal As String, ByVal registru As String, ByVal foaie As String, _
                              ByVal camp As String, ByVal dest As String, ByVal prefix As String, _
                              ByVal suprascrie As Boolean) As String
    Dim doc As Document, alerte As Long
    If Len(Dir(principal)) = 0 Or Len(Dir(registru)) = 0 Then
        ImbinaFisiere = "EROARE: documentul principal sau registrul de date nu exista."
        Exit Function
    End If
    alerte = Application.DisplayAlerts
    On Error GoTo Eroare
    Application.DisplayAlerts = wdAlertsNone
    Set doc = Documents.Open(FileName:=principal, ReadOnly:=True, AddToRecentFiles:=False, Visible:=False)
    doc.MailMerge.OpenDataSource Name:=registru, ConfirmConversions:=False, ReadOnly:=True, _
        LinkToSource:=True, AddToRecentFiles:=False, Revert:=False, Format:=wdOpenFormatAuto, _
        Connection:="Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" & registru & ";Mode=Read;", _
        SQLStatement:="SELECT * FROM `" & foaie & "$`", SubType:=wdMergeSubTypeAccess
    ImbinaFisiere = ImbinaInPDF(doc, camp, dest, prefix, suprascrie)
Curatare:
    On Error Resume Next
    If Not doc Is Nothing Then doc.Close SaveChanges:=wdDoNotSaveChanges
    On Error GoTo 0
    Application.DisplayAlerts = alerte
    Exit Function
Eroare:
    ImbinaFisiere = "EROARE: " & Err.Description
    Resume Curatare
End Function

Public Function ImbinaInPDF(ByVal doc As Document, ByVal camp As String, ByVal dest As String, _
                            ByVal prefix As String, ByVal suprascrie As Boolean) As String
    Dim mm As MailMerge, n As Long, i As Long, nume As String, fis As String, rezultat As Document
    Dim facute As Long, sarite As Long, alerte As Long, vazute As Object

    If Right$(dest, 1) <> "\" Then dest = dest & "\"
    If Len(Dir(dest, vbDirectory)) = 0 Then ImbinaInPDF = "EROARE: folderul nu exista: " & dest: Exit Function
    Set mm = doc.MailMerge
    If mm.State <> wdMainAndDataSource Then ImbinaInPDF = "EROARE: documentul nu are sursa de date.": Exit Function

    alerte = Application.DisplayAlerts
    On Error GoTo Eroare
    Application.ScreenUpdating = False
    Application.DisplayAlerts = wdAlertsNone

    If Not ExistaCamp(mm, camp) Then
        ImbinaInPDF = "EROARE: sursa de date nu are coloana '" & camp & "'."
        GoTo Curatare
    End If
    mm.DataSource.ActiveRecord = wdLastRecord
    n = mm.DataSource.ActiveRecord
    Set vazute = CreateObject("Scripting.Dictionary")
    For i = 1 To n
        mm.DataSource.ActiveRecord = i
        nume = NumeFisierSigur(CStr(mm.DataSource.DataFields(camp).Value))
        If vazute.Exists(LCase$(nume)) Then nume = nume & "_" & i ' doua randuri cu acelasi nume
        Do While vazute.Exists(LCase$(nume))
            nume = nume & "_"
        Loop
        vazute(LCase$(nume)) = True
        fis = dest & prefix & "_" & nume & ".pdf"
        If Len(Dir(fis)) > 0 And Not suprascrie Then
            sarite = sarite + 1
        Else
            mm.Destination = wdSendToNewDocument
            mm.SuppressBlankLines = True
            mm.DataSource.FirstRecord = i
            mm.DataSource.LastRecord = i
            mm.Execute Pause:=False
            Set rezultat = ActiveDocument ' Execute nu intoarce documentul; cel nou devine activ
            rezultat.ExportAsFixedFormat OutputFileName:=fis, ExportFormat:=wdExportFormatPDF, _
                OpenAfterExport:=False, OptimizeFor:=wdExportOptimizeForPrint, _
                Item:=wdExportDocumentContent, IncludeDocProps:=False, _
                CreateBookmarks:=wdExportCreateHeadingBookmarks, DocStructureTags:=True
            rezultat.Close SaveChanges:=wdDoNotSaveChanges
            Set rezultat = Nothing
            facute = facute + 1
        End If
    Next i
    ImbinaInPDF = "OK: " & facute & " PDF-uri create in " & dest & _
        IIf(sarite > 0, vbLf & sarite & " existente pastrate.", "")
Curatare:
    Application.DisplayAlerts = alerte
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    ImbinaInPDF = "EROARE: " & Err.Description & " (inregistrarea " & i & ")"
    On Error Resume Next
    If Not rezultat Is Nothing Then rezultat.Close SaveChanges:=wdDoNotSaveChanges
    On Error GoTo 0
    Resume Curatare
End Function

Private Function ExistaCamp(ByVal mm As MailMerge, ByVal camp As String) As Boolean
    Dim f As MailMergeFieldName
    For Each f In mm.DataSource.FieldNames
        If StrComp(f.Name, camp, vbTextCompare) = 0 Then ExistaCamp = True: Exit Function
    Next f
End Function

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
