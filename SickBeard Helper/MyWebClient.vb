Imports System.Net

Public Class MyWebClient
    Inherits WebClient
    Protected Overrides Function GetWebRequest(address As Uri) As WebRequest
        Dim request As HttpWebRequest = TryCast(MyBase.GetWebRequest(address), HttpWebRequest)
        request.AutomaticDecompression = DecompressionMethods.Deflate Or DecompressionMethods.GZip
        Return request
    End Function
End Class
