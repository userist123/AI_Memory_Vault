Attribute VB_Name = "Word_StiluriLot"
' =============================================================================
' Modul:    Word_StiluriLot
' Scop:     Aplica stilurile unui document-model (fonturi, titluri, spatieri) pe
'           toate documentele dintr-un folder si seteaza limba romana pentru
'           corectura. Textul ramane la fel; se schimba doar aspectul stilurilor.
' Rulare:   Alt+F8 > StiluriLot > alegeti modelul (.dotx/.docx), folderul cu
'           documente si folderul de rezultat.
' Modifica: NU modifica originalele: salveaza copii in folderul de rezultat
'           (fisierele existente se sar, daca nu confirmati suprascrierea).
'           Formatarea directa (aldine puse de mana etc.) se pastreaza.
' Versiune: 1.0
' =============================================================================
Option Explicit

Private Const LIMBA_RO As Long = 1048 ' wdRomanian

Public Sub StiluriLot()
    Dim model As String, sursa As String, dest As String, rasp As VbMsgBoxResult, rez As String
    On Error GoTo Eroare
    With Application.FileDialog(msoFileDialogFilePicker)
        .Title = "Alegeti documentul-model cu stilurile dorite"
        .Filters.Clear
        .Filters.Add "Documente Word", "*.dotx;*.dotm;*.docx;*.docm"
        If .Show <> -1 Then Exit Sub
        model = .SelectedItems(1)
    End With
    sursa = AlegeFolder("Folderul cu documentele de reformatat")
    If Len(sursa) = 0 Then Exit Sub
    dest = AlegeFolder("Folderul in care salvez copiile")
    If Len(dest) = 0 Then Exit Sub
    rasp = MsgBox("Suprascriu copiile existente?" & vbLf & "Nu = le sar.", vbYesNoCancel + vbQuestion, "Stiluri")
    If rasp = vbCancel Then Exit Sub
    rez = AplicaStiluriFolder(model, sursa, dest, rasp = vbYes)
    MsgBox rez, IIf(Left$(rez, 2) = "OK", vbInformation, vbExclamation), "Stiluri"
    Exit Sub
Eroare:
    MsgBox "Aplicarea stilurilor nu a reusit: " & Err.Description, vbCritical, "Stiluri"
End Sub

Public Function AplicaStiluriFolder(ByVal model As String, ByVal sursa As String, ByVal dest As String, _
                                    ByVal suprascrie As Boolean) As String
    Dim v As Variant, fis As String, doc As Document, alerte As Long, secAuto As Long, nFis As Long, sarite As Long
    Dim story As Range

    If Right$(sursa, 1) <> "\" Then sursa = sursa & "\"
    If Right$(dest, 1) <> "\" Then dest = dest & "\"
    If Len(Dir(model)) = 0 Then AplicaStiluriFolder = "EROARE: modelul nu exista: " & model: Exit Function
    If Len(Dir(sursa, vbDirectory)) = 0 Or Len(Dir(dest, vbDirectory)) = 0 Then
        AplicaStiluriFolder = "EROARE: folderul sursa sau cel de rezultat nu exista."
        Exit Function
    End If
    If StrComp(sursa, dest, vbTextCompare) = 0 Then
        AplicaStiluriFolder = "EROARE: folderul de rezultat trebuie sa fie altul decat sursa."
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
        If Left$(fis, 2) <> "~$" And StrComp(sursa & fis, model, vbTextCompare) <> 0 Then
            If Len(Dir(dest & fis)) > 0 And Not suprascrie Then
                sarite = sarite + 1
            Else
                Set doc = Documents.Open(FileName:=sursa & fis, ReadOnly:=True, AddToRecentFiles:=False, Visible:=False)
                doc.CopyStylesFromTemplate Template:=model
                For Each story In doc.StoryRanges
                    Do
                        story.LanguageID = LIMBA_RO
                        Set story = story.NextStoryRange
                    Loop Until story Is Nothing
                Next story
                doc.Styles(wdStyleNormal).LanguageID = LIMBA_RO
                doc.SaveAs2 FileName:=dest & fis, AddToRecentFiles:=False
                doc.Close SaveChanges:=wdDoNotSaveChanges
                Set doc = Nothing
                nFis = nFis + 1
            End If
        End If
    Next v

    AplicaStiluriFolder = "OK: stilurile din model au fost aplicate pe " & nFis & " documente." & _
        IIf(sarite > 0, vbLf & sarite & " copii existente pastrate.", "")
Curatare:
    Application.AutomationSecurity = secAuto
    Application.DisplayAlerts = alerte
    Application.ScreenUpdating = True
    Exit Function
Eroare:
    AplicaStiluriFolder = "EROARE: " & Err.Description & " (document: " & fis & ")"
    On Error Resume Next
    If Not doc Is Nothing Then doc.Close SaveChanges:=wdDoNotSaveChanges
    On Error GoTo 0
    Resume Curatare
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
