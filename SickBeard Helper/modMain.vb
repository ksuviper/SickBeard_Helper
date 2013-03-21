Imports System.Data.SQLite
Imports System.Xml
Imports System.IO
Imports System.Text
Imports System.Net
Imports System.Reflection

Module modMain
    Public dbConn As SQLite.SQLiteConnection = New SQLite.SQLiteConnection
    Public gsSBPath As String = ""
    Public gsTorrentDir As String = ""
    Public gsAppPath As String = Path.GetDirectoryName(Assembly.GetExecutingAssembly.Location()) & "\"

    Function GetSettings() As Boolean
        Dim objKey As Microsoft.Win32.RegistryKey
        Dim lsData As String = ""

        objKey = My.Computer.Registry.LocalMachine.OpenSubKey("Software\SB_Helper\", True)
        If objKey Is Nothing Then
            objKey = My.Computer.Registry.LocalMachine.CreateSubKey("Software\SB_Helper\")
        End If
        gsSBPath = objKey.GetValue("SickBeardPath", "").ToString()

        If gsSBPath.Length = 0 Then
            If My.Computer.FileSystem.DirectoryExists("C:\SickBeard") Then
                gsSBPath = "C:\SickBeard\"
                objKey.SetValue("SickBeardPath", gsSBPath, Microsoft.Win32.RegistryValueKind.String)
                GetSettings = True
            End If
        End If

        If gsSBPath.Length = 0 Then
            Console.WriteLine("!!!! SickBeard Path is missing !!!!")
            Console.WriteLine("Enter the path to SickBeard (e.g. C:\SickBeard):")
            lsData = Console.ReadLine()

            While Not My.Computer.FileSystem.DirectoryExists(lsData)
                Console.WriteLine("Error: The path was not found. Please enter a valid path or type Cancel")
                lsData = Console.ReadLine()
                If lsData.ToUpper = "CANCEL" Then
                    GetSettings = False
                    Exit Function
                End If
            End While

            If Right(lsData, 1) <> "\" Then
                lsData = lsData & "\"
            End If

            gsSBPath = lsData
            objKey.SetValue("SickBeardPath", gsSBPath, Microsoft.Win32.RegistryValueKind.String)
            GetSettings = True

        Else
            GetSettings = True
        End If
        GetTorrentDir()
    End Function
    Sub GetTorrentDir()
        Dim objReader As StreamReader
        Dim lsData As String = ""

        objReader = My.Computer.FileSystem.OpenTextFileReader(gsSBPath & "config.ini")

        While Not lsData Is Nothing
            lsData = objReader.ReadLine()
            If lsData.StartsWith("torrent_dir") Then
                gsTorrentDir = lsData.Replace("torrent_dir = ", "")
                If Right(gsTorrentDir, 1) <> "\" Then
                    gsTorrentDir = gsTorrentDir + "\"
                End If
                Exit While
            End If
        End While
        objReader.Close()

    End Sub

    Sub OpenDatabase()
        dbConn.ConnectionString = "Data Source=" & gsSBPath & "sickbeard.db;"
        dbConn.Open()
    End Sub
    Sub Main()

        If Assembly.GetExecutingAssembly.Location.Contains("2.0") Then
            ConvertToStaticPath()
        Else
            CheckUpdates()
        End If

        If Not GetSettings() Then
            End
        End If
        OpenDatabase()

        CheckShows()

        dbConn.Close()
        dbConn = Nothing

    End Sub

    Sub CheckShows()
        Dim objCmd As SQLite.SQLiteCommand = dbConn.CreateCommand()
        Dim rsData As SQLite.SQLiteDataReader
        Dim lsQuery As String = ""

        lsQuery = "Select * from tv_shows where status = 'Continuing' And Paused = 0"
        If My.Application.CommandLineArgs.Count > 0 Then
            If My.Application.CommandLineArgs.Item(0).ToUpper = "ENDED" Then
                lsQuery = "Select * from tv_shows where status = 'Ended'"
            End If
        End If

        objCmd.CommandText = lsQuery
        rsData = objCmd.ExecuteReader(CommandBehavior.KeyInfo)

        While rsData.Read
            CheckEpisodes(rsData("tvdb_id"), rsData("show_name"), rsData("Airs") & "")
        End While
        rsData.Close()
        rsData = Nothing

        'Console.ReadKey() 'For Debugging
    End Sub

    Sub CheckEpisodes(ByVal liId As Integer, ByVal lsName As String, ByVal lsAirTimeData As String)
        Dim objCmd As SQLite.SQLiteCommand = dbConn.CreateCommand()
        Dim objCmdE As SQLite.SQLiteCommand = dbConn.CreateCommand()
        Dim rsData As SQLite.SQLiteDataReader
        Dim lsQuery As String = ""
        Dim lsEpisode As String = ""
        Dim arrProv(0) As String
        Dim llDate As Long = 0
        Dim arrAirTime() As String
        Dim lsAirTime As DateTime
        Dim liProv As Integer = 0
        Dim lbFound As Boolean = False

        lsName = lsName.Replace(":", "")
        'lsName = lsName.Replace("(", "")
        'lsName = lsName.Replace(")", "")
        llDate = DateDiff("d", "1/1/100", DateAdd("yyyy", 99, DateTime.Today)) + 1
        If lsAirTimeData.Length > 0 Then
            arrAirTime = lsAirTimeData.Split(" ")
            If arrAirTime.Length > 2 Then
                lsAirTime = DateTime.Today & " " & arrAirTime(1) & " " & arrAirTime(2)
            ElseIf InStr(arrAirTime(1), "|") > 0 Then
                arrAirTime(1) = Replace(arrAirTime(1).Substring(InStr(arrAirTime(1), "|")), "c", ":00 PM")
                lsAirTime = DateTime.Today & " " & arrAirTime(1)
            Else
                lsAirTime = DateTime.Today & " " & arrAirTime(1)
            End If
        End If

        'Disabled for now: On even hours, check KickAss First.  On odd hours, check BTJunkie first
        'If Fix(Hour(DateTime.Now) / 2) = Hour(DateTime.Now) / 2 Then
        arrProv(0) = "KAT"
        'arrProv(1) = "BTJ"
        'arrProv(1) = "EZR"
        'Else
        'arrProv(0) = "BTJ"
        'arrProv(1) = "KAT"
        'End If

        lsQuery = "Select * From tv_episodes Where showid = " & liId & " And (Status = 3 "
        'AirTime is already in Eastern, so it's checking an hour after the show's airtime
        If DateTime.Now >= lsAirTime Then
            lsQuery = lsQuery & "Or (Status = 1 And airdate <= " & llDate & ")) "
        Else
            lsQuery = lsQuery & "Or (Status = 1 And airdate < " & llDate & ")) "
        End If
        lsQuery = lsQuery & "Order By Season Desc, Episode Desc"
        objCmd.CommandText = lsQuery
        rsData = objCmd.ExecuteReader(CommandBehavior.KeyInfo)
        While rsData.Read

            lsEpisode = lsName & " S" & Format(rsData("season"), "00") & "E" & Format(rsData("episode"), "00")

            lbFound = False
            liProv = 0

            While (Not lbFound) And liProv < arrProv.Length
                lbFound = FindEpisode(lsEpisode, lsName, arrProv(liProv), rsData("Status"), rsData("season"), rsData("episode"))
                liProv += 1
            End While

            If lbFound Then
                lsQuery = "Update tv_episodes Set Status = 2 Where tvdbid = " & rsData("tvdbid")
                objCmdE.CommandText = lsQuery
                objCmdE.ExecuteNonQuery()
            Else
                Console.WriteLine("All providers failed to find the file.")
            End If

        End While
        rsData.Close()
        rsData = Nothing

    End Sub

    Function FindEpisode(ByVal lsSearch As String, ByVal lsName As String, ByVal lsProvider As String, ByVal liStatus As Integer, ByVal lsSeason As String, ByVal lsEpisode As String) As Boolean
        'Dim objWeb As WebClient = New WebClient()
        Dim objWeb As MyWebClient = New MyWebClient()
        Dim objResponse As HttpWebResponse
        Dim objRequest As HttpWebRequest
        Dim lsURL As String = ""
        Dim lsXML As String = ""
        Dim lsTitle As String = ""
        Dim lsTorrent As String = ""
        Dim lsHash As String = ""
        Dim liSeeds As Integer = 0
        Dim liVerified As Integer = 0
        Dim llSize As Long = 0
        Dim lbFound As Boolean = False
        Dim arrData(256) As Byte
        Dim llLength As Long = 0
        Dim liIndex As Integer = 0
        Dim lsData As String = ""
        Dim arrString() As String
        Dim liMinSeed As Integer = 5
        Dim bAltSearchCriteria As Boolean = False
        Dim objXMLSettings As Xml.XmlReaderSettings = New Xml.XmlReaderSettings

        Console.WriteLine("Looking For: " & lsSearch & " (Provider: " & lsProvider & ")")

        If lsProvider = "KAT" Then
            'lsURL = "http://www.kat.ph/new/?q=" & lsSearch & "&field=seeders&sorder=desc&rss=1"
            'lsURL = "http://kickasstorrents.com/usearch/" & lsName & " season:" & lsSeason & " episode:" & lsEpisode & "/?rss=1"
            lsURL = "http://kickasstorrents.com/usearch/" & lsName & " s" & CInt(lsSeason).ToString("D2") & "e" & CInt(lsEpisode).ToString("D2") & "/?rss=1"
        ElseIf lsProvider = "BTJ" Then
            lsURL = "http://btjunkie.org/rss.xml?q=" & lsSearch & "&o=52"
        ElseIf lsProvider = "EZR" Then
            lsURL = "http://www.ezrss.it/search/index.php?show_name=" & lsName & "&season=" & lsSeason
            lsURL = lsURL & "&episode=" & lsEpisode & "&mode=rss"
            objXMLSettings.ProhibitDtd = False
        End If
        Try            
            'objWeb.Encoding = System.Text.Encoding.UTF8
            lsXML = objWeb.DownloadString(lsURL)            

        Catch ex As Exception
            If ex.Message.Contains("(404)") Then
                Console.WriteLine("No suitable torrent found on provider: " & lsProvider & ".")
                FindEpisode = False
                Exit Function
                'bAltSearchCriteria = True
            Else
                Console.WriteLine("Hosed! Exception: " & ex.Message)
                FindEpisode = False
                Exit Function
            End If
        End Try

        'If bAltSearchCriteria Then
        '    If lsProvider = "KAT" Then
        '        'lsURL = "http://www.kat.ph/new/?q=" & lsSearch & "&field=seeders&sorder=desc&rss=1"
        '        lsURL = "http://kickasstorrents.com/usearch/" & lsName & " s" & CInt(lsSeason).ToString("D2") & "e" & CInt(lsEpisode).ToString("D2") & "/?rss=1"
        '    ElseIf lsProvider = "BTJ" Then
        '        lsURL = "http://btjunkie.org/rss.xml?q=" & lsSearch & "&o=52"
        '    ElseIf lsProvider = "EZR" Then
        '        lsURL = "http://www.ezrss.it/search/index.php?show_name=" & lsName & "&season=" & lsSeason
        '        lsURL = lsURL & "&episode=" & lsEpisode & "&mode=rss"
        '        objXMLSettings.ProhibitDtd = False
        '    End If

        '    Try
        '        'objWeb.Encoding = System.Text.Encoding.UTF8
        '        lsXML = objWeb.DownloadString(lsURL)

        '    Catch ex As Exception
        '        If ex.Message.Contains("(404)") Then
        '            Console.WriteLine("No suitable torrent found on provider: " & lsProvider & ".")
        '            FindEpisode = False
        '            Exit Function
        '        Else
        '            Console.WriteLine("Hosed! Exception: " & ex.Message)
        '            FindEpisode = False
        '            Exit Function
        '        End If
        '    End Try

        'End If

        lbFound = False

        Try
            Using objReader As XmlReader = XmlReader.Create(New StringReader(lsXML), objXMLSettings)
                objReader.ReadToFollowing("description")
                While objReader.ReadToFollowing("title")
                    lsTitle = objReader.ReadElementContentAsString()

                    If lsProvider = "KAT" Then
                        'objReader.ReadToFollowing("torrentLink")
                        'lsTorrent = objReader.ReadElementContentAsString()                        
                        'objReader.MoveToAttribute("length")
                        'llSize = objReader.Value
                        objReader.ReadToFollowing("torrent:infoHash")
                        lsHash = objReader.ReadElementContentAsString()
                        objReader.ReadToFollowing("torrent:seeds")
                        liSeeds = objReader.ReadElementContentAsString()
                        'objReader.ReadToFollowing("size")
                        'llSize = objReader.ReadElementContentAsString()
                        objReader.ReadToFollowing("torrent:verified")
                        liVerified = objReader.ReadElementContentAsString()
                        objReader.ReadToFollowing("enclosure")
                        objReader.MoveToAttribute("url")
                        lsTorrent = objReader.Value
                        objReader.MoveToAttribute("length")
                        llSize = objReader.Value
                    ElseIf lsProvider = "BTJ" Then
                        liIndex = lsTitle.LastIndexOf("[")
                        lsData = lsTitle.Substring(liIndex)
                        lsData = lsData.Replace("[", "").Replace("]", "")
                        arrString = lsData.Split("/")
                        If Not IsNumeric(arrString(0)) Then
                            liSeeds = 0
                        Else
                            liSeeds = CInt(arrString(0))
                        End If
                        lsTitle = Trim(Left(lsTitle, liIndex - 1))
                        objReader.ReadToFollowing("guid")
                        lsData = objReader.ReadElementContentAsString()
                        liIndex = lsData.LastIndexOf("/")
                        lsHash = lsData.Substring(liIndex + 1)
                        objReader.ReadToFollowing("enclosure")
                        objReader.MoveToAttribute("url")
                        lsTorrent = objReader.Value
                        objReader.MoveToAttribute("length")
                        llSize = objReader.Value
                        If liSeeds > 0 Then
                            liVerified = 1
                        Else
                            liVerified = 0
                        End If
                    ElseIf lsProvider = "EZR" Then
                        lsTitle = lsTitle.Replace("<![CDATA[", "").Replace("]", "").Replace("[", "")
                        liSeeds = 100
                        liVerified = 1
                        objReader.ReadToFollowing("enclosure")
                        objReader.MoveToAttribute("url")
                        lsTorrent = objReader.Value
                        objReader.MoveToAttribute("length")
                        llSize = objReader.Value
                        objReader.ReadToFollowing("infoHash")
                        lsHash = objReader.ReadElementContentAsString()
                    End If

                    'Certain characters are not legal in folder/file names
                    lsTitle = lsTitle.Replace("\", "").Replace("/", "")
                    lsTitle = lsTitle.Replace("<", "").Replace(">", "")
                    lsTitle = lsTitle.Replace("""", "")
                    lsTitle = lsTitle.Replace(":", "")
                    lsTitle = lsTitle.Replace("*", "")
                    lsTitle = lsTitle.Replace("?", "")
                    lsTitle = lsTitle.Replace("|", "")
                    lsTitle = lsTitle.Replace(";", "")
                    lsTitle = lsTitle.Replace(".", " ")

                    lsName = lsName.Replace("\", "").Replace("/", "")
                    lsName = lsName.Replace("<", "").Replace(">", "")
                    lsName = lsName.Replace("""", "")
                    lsName = lsName.Replace(":", "")
                    lsName = lsName.Replace("*", "")
                    lsName = lsName.Replace("?", "")
                    lsName = lsName.Replace("|", "")
                    lsName = lsName.Replace(";", "")
                    lsName = lsName.Replace("(US)", "")
                    lsName = lsName.Replace(".", " ")
                    lsName = Trim(lsName)

                    'Try to determine if it is the correct show and a good copy
                    If (lsTitle.ToUpper.StartsWith(lsName.ToUpper) Or _
                        lsTitle.ToUpper.StartsWith(lsName.ToUpper.Replace("(", "").Replace(")", ""))) And _
                    (lsTitle.ToUpper.Contains(Right(lsSearch.ToUpper, 6)) Or _
                    lsTitle.ToUpper.Contains(lsSeason & "X" & lsEpisode) Or _
                    lsTitle.ToUpper.Contains(lsSeason & lsEpisode)) And _
                    Not (lsTitle.ToUpper.Contains("SWESUB") Or lsTitle.ToUpper.Contains("NLSUB")) And _
                    Not (lsTitle.ToUpper.Contains("SPANISH") Or lsTitle.ToUpper.Contains("DUTCH")) And _
                    Not (lsTitle.ToUpper.Contains("FRENCH")) Then

                        If (liVerified = 1 Or (lsProvider = "KAT" And liSeeds > 10) Or _
                        ((lsTitle.ToUpper.Contains("XVID-LOL") Or lsTitle.ToUpper.Contains("EZTV") Or _
                        lsTitle.ToUpper.Contains("[VTV]")) And liSeeds > 0)) _
                        And ((llSize > 99000000 And llSize < 500000000) Or lsProvider = "EZR") Then 'Standard def (larger than 99mb and less than 500mb)

                            'Make sure we haven't already tried this one
                            If My.Computer.FileSystem.FileExists(gsTorrentDir & lsTitle & "_" & lsHash & ".torrent.loaded") Then
                                lbFound = False
                            ElseIf My.Computer.FileSystem.FileExists(gsTorrentDir & lsTitle & "_" & lsHash & ".torrent") Then
                                lbFound = False
                            Else
                                If liStatus = 1 And liSeeds < 100 Then 'If we are getting something that just aired, check seeders
                                    lbFound = False
                                Else
                                    lbFound = True
                                    Exit While
                                End If
                            End If
                            'Else 'check for hi-def version if no std-def
                            '    If (liVerified = 1 Or (lsProvider = "KAT" And liSeeds > 10) Or _
                            '    ((lsTitle.ToUpper.Contains("XVID-LOL") Or lsTitle.ToUpper.Contains("EZTV") Or _
                            '    lsTitle.ToUpper.Contains("[VTV]")) And liSeeds > 0)) _
                            '    And ((llSize > 500000000 And llSize < 1000000000) Or lsProvider = "EZR") Then 'High def (larger than 500mb and less than 1gb)

                            '        'Make sure we haven't already tried this one
                            '        If My.Computer.FileSystem.FileExists(gsTorrentDir & lsTitle & "_" & lsHash & ".torrent.loaded") Then
                            '            lbFound = False
                            '        ElseIf My.Computer.FileSystem.FileExists(gsTorrentDir & lsTitle & "_" & lsHash & ".torrent") Then
                            '            lbFound = False
                            '        Else
                            '            If liStatus = 1 And liSeeds < 100 Then 'If we are getting something that just aired, check seeders
                            '                lbFound = False
                            '            Else
                            '                lbFound = True
                            '                Exit While
                            '            End If
                            '        End If
                            '    End If
                        End If
                    End If
                End While
            End Using
        Catch ex As Exception
            Console.WriteLine("Hosed! Exception: " & ex.Message)
            lbFound = False
        End Try

        If lbFound Then
            objRequest = WebRequest.Create(lsTorrent)
            'If lsProvider = "KAT" Then
            '    objRequest.Headers.Add(System.Net.HttpRequestHeader.Referer, "http://kat.ph/")
            'End If
            objRequest.AutomaticDecompression = DecompressionMethods.Deflate Or DecompressionMethods.GZip
            Try
                objResponse = objRequest.GetResponse
                If objResponse.StatusCode = Net.HttpStatusCode.OK Then
                    Dim objReader As IO.Stream = objResponse.GetResponseStream()
                    Dim objWriter As New IO.FileStream(gsTorrentDir & lsTitle & "_" & lsHash & ".torrent_tmp", IO.FileMode.Create)

                    Do
                        llLength = objReader.Read(arrData, 0, arrData.Length)
                        objWriter.Write(arrData, 0, llLength)
                    Loop While llLength > 0
                    objWriter.Close()
                    objReader.Close()
                    objWriter = Nothing
                    objReader = Nothing

                    My.Computer.FileSystem.RenameFile( _
                    gsTorrentDir & lsTitle & "_" & lsHash & ".torrent_tmp", _
                    lsTitle & "_" & lsHash & ".torrent")

                    Console.WriteLine("Snatched " & lsTitle)
                    FindEpisode = True
                Else
                    Console.WriteLine("Hosed! Server returned: ", _
                    objResponse.StatusCode & " - " & objResponse.StatusDescription)
                    FindEpisode = False
                End If
            Catch ex As Exception
                Console.WriteLine("Hosed! Exception: " & ex.Message)
                FindEpisode = False
            End Try

        Else
            Console.WriteLine("No suitable torrent found on provider: " & lsProvider & ".")
            FindEpisode = False
        End If

    End Function

    Sub ConvertToStaticPath()
        Dim lsData As String = ""

        Console.WriteLine("********* The program needs to be moved to a new static folder *********")
        Console.WriteLine("Enter the path where you want it to reside, or press ENTER for the default: C:\SB_Helper\")
        lsData = Console.ReadLine()
        If lsData.Length = 0 Then
            lsData = "C:\SB_Helper\"
        End If
        If Right(lsData, 1) <> "\" Then
            lsData = lsData & "\"
        End If

        Try
            If Not My.Computer.FileSystem.DirectoryExists(lsData) Then
                My.Computer.FileSystem.CreateDirectory(lsData)
            End If

            With My.Computer.FileSystem
                .CopyFile(Assembly.GetExecutingAssembly.Location(), lsData & Path.GetFileName(Assembly.GetExecutingAssembly.Location()))
                .CopyFile(gsAppPath & "System.Data.SQLite.dll", lsData & "System.Data.SQLite.dll")
            End With

        Catch ex As Exception
            Console.WriteLine("Hosed! Exception: " & ex.Message)
            Console.WriteLine("Press any key to exit.")
            Console.ReadKey()
            End
        End Try


    End Sub

    Sub CheckUpdates()
        Dim lsVer As String = ""
        Dim objWeb As WebClient = New WebClient()
        Dim objVer As FileVersionInfo

        objVer = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly.Location())

        Try
            'lsVer = objWeb.DownloadString("http://www.korbfamily.net/library/sb_helper/ver.htm")
            lsVer = objWeb.DownloadString("http://www.xtremecs.net/sb_helper/ver.htm")
        Catch ex As Exception
            Console.WriteLine("Unable to check updates.  Skipping.")
        End Try

        If lsVer.Length > 0 And Not Assembly.GetExecutingAssembly.Location().ToUpper.Contains("DEBUG") Then
            If lsVer <> objVer.ProductVersion Then
                Console.WriteLine("Update found.  Downloading...")
                DoUpdate()
            End If
        End If

    End Sub

    Sub DoUpdate()
        Dim objWeb As WebClient = New WebClient()
        Dim objWriter As StreamWriter

        Try
            'objWeb.DownloadFile("http://www.korbfamily.net/library/sb_helper/SB_Helper.exe.deploy", gsAppPath & "SB_Helper.exe.deploy")
            objWeb.DownloadFile("http://www.xtremecs.net/sb_helper/SB_Helper.exe.deploy", gsAppPath & "SB_Helper.exe.deploy")
            objWriter = My.Computer.FileSystem.OpenTextFileWriter(gsAppPath & "Update.cmd", False)
            Console.WriteLine("Installing update...")
            objWriter.WriteLine("@Echo off")
            objWriter.WriteLine(":StartCheck")
            objWriter.WriteLine("taskkill /f /IM sb_helper.exe >NUL")
            objWriter.WriteLine("tasklist | find /i ""sb_helper.exe"" >NUL")
            objWriter.WriteLine("if %errorlevel%==0 goto :StartCheck")
            objWriter.WriteLine("del " & gsAppPath & "SB_Helper.exe")
            objWriter.WriteLine("ren " & gsAppPath & "SB_Helper.exe.deploy " & "SB_Helper.exe")
            objWriter.WriteLine(gsAppPath & "SB_Helper.exe")
            objWriter.Close()
            Shell(gsAppPath & "Update.cmd", AppWinStyle.NormalFocus, False)
            End
        Catch ex As Exception
            Console.WriteLine("Error downloading update.  We'll try again next time.")
        End Try
    End Sub

End Module
