Imports System.Collections.ObjectModel
Imports System.Text.Json.Serialization

Public Class TitleIndex
    <JsonPropertyName("source")>
    Public Property Source As String

    <JsonPropertyName("titles")>
    Public Property Titles As List(Of TitleIndexEntry)
End Class

Public Class TitleIndexEntry
    <JsonPropertyName("number")>
    Public Property Number As Integer

    <JsonPropertyName("name")>
    Public Property Name As String

    <JsonPropertyName("file")>
    Public Property File As String

    <JsonPropertyName("pdf")>
    Public Property Pdf As String

    Public ReadOnly Property DisplayName As String
        Get
            Return $"Title {Number} — {Name}"
        End Get
    End Property
End Class

Public Class TitleDocument
    <JsonPropertyName("titleNumber")>
    Public Property TitleNumber As Integer

    <JsonPropertyName("titleName")>
    Public Property TitleName As String

    <JsonPropertyName("sourcePdf")>
    Public Property SourcePdf As String

    <JsonPropertyName("root")>
    Public Property Root As StatuteNodeDto
End Class

Public Class StatuteNodeDto
    <JsonPropertyName("id")>
    Public Property Id As String

    <JsonPropertyName("kind")>
    Public Property Kind As String

    <JsonPropertyName("number")>
    Public Property Number As String

    <JsonPropertyName("heading")>
    Public Property Heading As String

    <JsonPropertyName("text")>
    Public Property Text As String

    <JsonPropertyName("children")>
    Public Property Children As List(Of StatuteNodeDto)
End Class

''' <summary>
''' UI-facing tree node with lazy children binding support.
''' </summary>
Public Class StatuteNode
    Public Property Id As String
    Public Property Kind As String
    Public Property Number As String
    Public Property Heading As String
    Public Property Text As String
    Public Property Parent As StatuteNode
    Public Property Children As ObservableCollection(Of StatuteNode) =
        New ObservableCollection(Of StatuteNode)()

    Public ReadOnly Property Label As String
        Get
            Select Case Kind
                Case "title"
                    Return $"Title {Number} — {Heading}"
                Case "part"
                    Return If(String.IsNullOrWhiteSpace(Heading),
                              $"Part {Number}",
                              $"Part {Number}. {Heading}")
                Case "chapter"
                    Return If(String.IsNullOrWhiteSpace(Heading),
                              $"Chapter {Number}",
                              $"Chapter {Number}. {Heading}")
                Case "subchapter"
                    Return If(String.IsNullOrWhiteSpace(Heading),
                              $"Subchapter {Number}",
                              $"Subchapter {Number}. {Heading}")
                Case "section"
                    Return If(String.IsNullOrWhiteSpace(Heading),
                              $"§ {Number}",
                              $"§ {Number}. {Heading}")
                Case Else
                    Return If(String.IsNullOrWhiteSpace(Heading), Number, $"{Number} {Heading}".Trim())
            End Select
        End Get
    End Property

    Public ReadOnly Property KindLabel As String
        Get
            Select Case Kind
                Case "title" : Return "Title"
                Case "part" : Return "Part"
                Case "chapter" : Return "Chapter"
                Case "subchapter" : Return "Subchapter"
                Case "section" : Return "Section"
                Case "unit" : Return "Subdivision"
                Case Else : Return Kind
            End Select
        End Get
    End Property

    Public ReadOnly Property PathLabel As String
        Get
            Dim parts As New List(Of String)()
            Dim current As StatuteNode = Me
            While current IsNot Nothing
                parts.Insert(0, current.Label)
                current = current.Parent
            End While
            Return String.Join(" › ", parts)
        End Get
    End Property

    ''' <summary>
    ''' Full text including nested subdivisions, for the reader pane.
    ''' </summary>
    Public Function FormatFullText() As String
        Dim sb As New System.Text.StringBuilder()
        AppendFormatted(sb, 0)
        Return sb.ToString().Trim()
    End Function

    Private Sub AppendFormatted(sb As System.Text.StringBuilder, depth As Integer)
        Dim indent = New String(" "c, depth * 2)
        sb.AppendLine(indent & Label)
        sb.AppendLine()
        If Not String.IsNullOrWhiteSpace(Text) Then
            For Each para In Text.Replace(vbCrLf, vbLf).Split({vbLf}, StringSplitOptions.None)
                Dim trimmed = para.Trim()
                If trimmed.Length = 0 Then
                    sb.AppendLine()
                Else
                    sb.AppendLine(indent & trimmed)
                End If
            Next
            sb.AppendLine()
        End If
        For Each child In Children
            child.AppendFormatted(sb, depth + 1)
        Next
    End Sub

    Public Shared Function FromDto(dto As StatuteNodeDto, Optional parent As StatuteNode = Nothing) As StatuteNode
        Dim node As New StatuteNode With {
            .Id = dto.Id,
            .Kind = dto.Kind,
            .Number = If(dto.Number, ""),
            .Heading = If(dto.Heading, ""),
            .Text = If(dto.Text, ""),
            .Parent = parent
        }
        If dto.Children IsNot Nothing Then
            For Each childDto In dto.Children
                node.Children.Add(FromDto(childDto, node))
            Next
        End If
        Return node
    End Function
End Class

Public Class SearchHit
    Public Property Node As StatuteNode
    Public Property Snippet As String

    Public ReadOnly Property PathLabel As String
        Get
            Return Node.PathLabel
        End Get
    End Property

    Public ReadOnly Property KindLabel As String
        Get
            Return Node.KindLabel
        End Get
    End Property
End Class
