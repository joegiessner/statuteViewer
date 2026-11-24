Imports System.Collections.ObjectModel

''' <summary>
''' Placeholder tree node for a title. Children load from disk on first expand.
''' </summary>
Public Class LazyTitleNode
    Inherits StatuteNode

    Private ReadOnly _repository As StatuteRepository
    Private ReadOnly _titleNumber As Integer
    Private _loaded As Boolean

    Public Sub New(repository As StatuteRepository, titleNumber As Integer, titleName As String)
        _repository = repository
        _titleNumber = titleNumber
        Id = $"title-{titleNumber}"
        Kind = "title"
        Number = titleNumber.ToString()
        Heading = titleName
        Text = ""
        ' Dummy child so the expand arrow appears before load.
        Children.Add(New StatuteNode With {
            .Id = $"placeholder-{titleNumber}",
            .Kind = "unit",
            .Number = "",
            .Heading = "Loading...",
            .Text = ""
        })
    End Sub

    Public ReadOnly Property IsLoaded As Boolean
        Get
            Return _loaded
        End Get
    End Property

    Public Function EnsureLoaded() As StatuteNode
        If _loaded Then
            Return Me
        End If

        Dim root = _repository.LoadTitle(_titleNumber)
        Children.Clear()
        For Each child In root.Children
            child.Parent = Me
            Children.Add(child)
        Next
        Text = root.Text
        _loaded = True
        Return Me
    End Function
End Class
