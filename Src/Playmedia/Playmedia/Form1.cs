using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using System.Management;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Drawing;
using System.ComponentModel;
using EO.WebBrowser;
using System.Media;
using System.Globalization;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using NetFwTypeLib;
using CSCore.Streams;
using CSCore.SoundIn;
using CSCore;
using CSCore.Codecs;
using CSCore.DSP;
using CSCore.SoundOut;
using CSCore.Streams.Effects;
using CSCore.CoreAudioAPI;
using WinformsVisualization.Visualization;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CSCore.Codecs.WAV;
namespace Playmedia
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        public int numBars = 20;
        public float[] barData = new float[20];
        public int minFreq = 1;
        public int maxFreq = 20000;
        public int barSpacing = 0;
        public bool logScale = true;
        public bool isAverage = false;
        public float highScaleAverage = 1.0f;
        public float highScaleNotAverage = 2.0f;
        public LineSpectrum lineSpectrum;
        public WasapiCapture capture;
        public FftSize fftSize;
        public float[] fftBuffer;
        public BasicSpectrumProvider spectrumProvider;
        public IWaveSource finalSource;
        public static string backgroundcolor = "";
        public static bool closed = false;
        public void Form1_Shown(object sender, EventArgs e)
        {
            this.pictureBox1.Dock = DockStyle.Fill;
            EO.WebEngine.BrowserOptions options = new EO.WebEngine.BrowserOptions();
            options.EnableWebSecurity = false;
            EO.WebBrowser.Runtime.DefaultEngineOptions.SetDefaultBrowserOptions(options);
            EO.WebEngine.Engine.Default.Options.AllowProprietaryMediaFormats();
            EO.WebEngine.Engine.Default.Options.SetDefaultBrowserOptions(new EO.WebEngine.BrowserOptions
            {
                EnableWebSecurity = false
            });
            this.webView1.Create(pictureBox1.Handle);
            this.webView1.Engine.Options.AllowProprietaryMediaFormats();
            this.webView1.SetOptions(new EO.WebEngine.BrowserOptions
            {
                EnableWebSecurity = false
            });
            this.webView1.Engine.Options.DisableGPU = false;
            this.webView1.Engine.Options.DisableSpellChecker = true;
            this.webView1.Engine.Options.CustomUserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";
            this.webView1.JSInitCode = @"setInterval(function(){ 
                        try {
                            if (window.location.href.indexOf('youtube') > -1) {
                                document.cookie='VISITOR_INFO1_LIVE = oKckVSqvaGw; path =/; domain =.youtube.com';
                                var cookies = document.cookie.split('; ');
                                for (var i = 0; i < cookies.length; i++)
                                {
                                    var cookie = cookies[i];
                                    var eqPos = cookie.indexOf('=');
                                    var name = eqPos > -1 ? cookie.substr(0, eqPos) : cookie;
                                    document.cookie = name + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT';
                                }
                                var el = document.getElementsByClassName('ytp-ad-skip-button');
                                for (var i=0;i<el.length; i++) {
                                    el[i].click();
                                }
                                var element = document.getElementsByClassName('ytp-ad-overlay-close-button');
                                for (var i=0;i<element.length; i++) {
                                    element[i].click();
                                }
                            }
                        }
                        catch {}
                    }, 5000);";
            Navigate("");
            string folderpath = "file:///" + System.Reflection.Assembly.GetEntryAssembly().Location.Replace(@"file:\", "").Replace(Process.GetCurrentProcess().ProcessName + ".exe", "").Replace(@"\", "/").Replace(@"//", "");
            string path = @"playmedia.html";
            string readText = DecryptFiles(path + ".encrypted", "tybtrybrtyertu50727885").Replace("file:///C:/playmedia/", folderpath);
            webView1.LoadHtml(readText);
            webView1.RegisterJSExtensionFunction("savePosition", new JSExtInvokeHandler(WebView_JSSavePosition));
            webView1.RegisterJSExtensionFunction("getPosition", new JSExtInvokeHandler(WebView_JSGetPosition));
        }
        public static string DecryptFiles(string inputFile, string password)
        {
            using (var input = File.OpenRead(inputFile))
            {
                byte[] salt = new byte[8];
                input.Read(salt, 0, salt.Length);
                using (var decryptedStream = new MemoryStream())
                using (var pbkdf = new Rfc2898DeriveBytes(password, salt))
                using (var aes = new RijndaelManaged())
                using (var decryptor = aes.CreateDecryptor(pbkdf.GetBytes(aes.KeySize / 8), pbkdf.GetBytes(aes.BlockSize / 8)))
                using (var cs = new CryptoStream(input, decryptor, CryptoStreamMode.Read))
                {
                    string contents;
                    int data;
                    while ((data = cs.ReadByte()) != -1)
                        decryptedStream.WriteByte((byte)data);
                    decryptedStream.Position = 0;
                    using (StreamReader sr = new StreamReader(decryptedStream))
                        contents = sr.ReadToEnd();
                    decryptedStream.Flush();
                    return contents;
                }
            }
        }
        public void GetAudioByteArray()
        {
            capture = new WasapiLoopbackCapture();
            capture.Initialize();
            IWaveSource source = new SoundInSource(capture);
            fftSize = FftSize.Fft4096;
            fftBuffer = new float[(int)fftSize];
            spectrumProvider = new BasicSpectrumProvider(capture.WaveFormat.Channels, capture.WaveFormat.SampleRate, fftSize);
            lineSpectrum = new LineSpectrum(fftSize)
            {
                SpectrumProvider = spectrumProvider,
                UseAverage = true,
                BarCount = numBars,
                BarSpacing = 2,
                IsXLogScale = false,
                ScalingStrategy = ScalingStrategy.Sqrt
            };
            var notificationSource = new SingleBlockNotificationStream(source.ToSampleSource());
            notificationSource.SingleBlockRead += NotificationSource_SingleBlockRead;
            finalSource = notificationSource.ToWaveSource();
            capture.DataAvailable += Capture_DataAvailable;
            capture.Start();
        }
        public void Capture_DataAvailable(object sender, DataAvailableEventArgs e)
        {
            finalSource.Read(e.Data, e.Offset, e.ByteCount);
        }
        public void NotificationSource_SingleBlockRead(object sender, SingleBlockReadEventArgs e)
        {
            spectrumProvider.Add(e.Left, e.Right);
        }
        public float[] GetFFtData()
        {
            lock (barData)
            {
                lineSpectrum.BarCount = numBars;
                if (numBars != barData.Length)
                {
                    barData = new float[numBars];
                }
            }
            if (spectrumProvider.IsNewDataAvailable)
            {
                lineSpectrum.MinimumFrequency = minFreq;
                lineSpectrum.MaximumFrequency = maxFreq;
                lineSpectrum.IsXLogScale = logScale;
                lineSpectrum.BarSpacing = barSpacing;
                lineSpectrum.SpectrumProvider.GetFftData(fftBuffer, this);
                return lineSpectrum.GetSpectrumPoints(100.0f, fftBuffer);
            }
            else
            {
                return null;
            }
        }
        public void ComputeData()
        {
            float[] resData = GetFFtData();
            int numBars = barData.Length;
            if (resData == null)
            {
                return;
            }
            lock (barData)
            {
                for (int i = 0; i < numBars && i < resData.Length; i++)
                {
                    barData[i] = resData[i] / 100.0f;
                }
                for (int i = 0; i < numBars && i < resData.Length; i++)
                {
                    if (lineSpectrum.UseAverage)
                    {
                        barData[i] = barData[i] + highScaleAverage * (float)Math.Sqrt(i / (numBars + 0.0f)) * barData[i];
                    }
                    else
                    {
                        barData[i] = barData[i] + highScaleNotAverage * (float)Math.Sqrt(i / (numBars + 0.0f)) * barData[i];
                    }
                }
            }
        }
        public void Start()
        {
            while (!closed)
            {
                if (this.WindowState != FormWindowState.Minimized)
                { 
                    try
                    {
                        ComputeData();
                        string stringinject = @"
            try {

                ctx.fillStyle = 'backgroundcolor';
                ctx.fillRect(0, 0, WIDTH, HEIGHT);
                var audiorawdata = [rawdata0, rawdata1, rawdata2, rawdata3, rawdata4, rawdata5, rawdata6, rawdata7, rawdata8, rawdata9, rawdataA, rawdataB, rawdataC, rawdataD, rawdataE, rawdataF, rawdataG, rawdataH, rawdataI, rawdataJ];
            
                if (!visualizeron) {
                    audiorawdata = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                }

                var len = audiorawdata.length;

                smoothred.push(Math.random());
                smoothgreen.push(Math.random());
                smoothblue.push(Math.random());

                if (smoothred.length > 4) {
                    smoothred.shift();
                }
                if (smoothgreen.length > 4) {
                    smoothgreen.shift();
                }
                if (smoothblue.length > 4) {
                    smoothblue.shift();
                }

                coefficientred =  average(smoothred);
                coefficientgreen =  average(smoothgreen);
                coefficientblue =  average(smoothblue);

                barWidth = (WIDTH / len);
                barHeight = HEIGHT;
                x = 0;

                for (var i = 0; i < len; i++) {

                    barHeight = audiorawdata[i];

                    var r = 255 * coefficientred;
                    var g = 255 * coefficientgreen;
                    var b = 255 * coefficientblue;

                    ctx.fillStyle = 'rgb(' + r + ',' + g + ',' + b + ')';
                    ctx.strokeStyle = 'rgb(' + r + ', ' + g + ', ' + b + ')';
                    ctx.fillRect(x + 0.5, HEIGHT - barHeight, barWidth - 1, barHeight);

                    x += barWidth;
      
                }

                ctx.stroke();

            }
            catch {}
    ";
                        this.webView1.EvalScript(stringinject.Replace("backgroundcolor", backgroundcolor).Replace("rawdata0", (barData[0] * 200f).ToString()).Replace("rawdata1", (barData[1] * 200f).ToString()).Replace("rawdata2", (barData[2] * 200f).ToString()).Replace("rawdata3", (barData[3] * 200f).ToString()).Replace("rawdata4", (barData[4] * 200f).ToString()).Replace("rawdata5", (barData[5] * 200f).ToString()).Replace("rawdata6", (barData[6] * 200f).ToString()).Replace("rawdata7", (barData[7] * 200f).ToString()).Replace("rawdata8", (barData[8] * 200f).ToString()).Replace("rawdata9", (barData[9] * 200f).ToString()).Replace("rawdataA", (barData[10] * 200f).ToString()).Replace("rawdataB", (barData[11] * 200f).ToString()).Replace("rawdataC", (barData[12] * 200f).ToString()).Replace("rawdataD", (barData[13] * 200f).ToString()).Replace("rawdataE", (barData[14] * 200f).ToString()).Replace("rawdataF", (barData[15] * 200f).ToString()).Replace("rawdataG", (barData[16] * 200f).ToString()).Replace("rawdataH", (barData[17] * 200f).ToString()).Replace("rawdataI", (barData[18] * 200f).ToString()).Replace("rawdataJ", (barData[19] * 200f).ToString()));
                    }
                    catch { }
                }
                System.Threading.Thread.Sleep(40);
            }
        }
        void WebView_JSSavePosition(object sender, JSExtInvokeArgs e)
        {
            string lat = e.Arguments[0] as string;
            string lng = e.Arguments[1] as string;
            string filename = e.Arguments[2] as string;
            using (StreamWriter createdfile = new StreamWriter(filename + ".txt"))
            {
                createdfile.WriteLine(lat);
                createdfile.WriteLine(lng);
            }
        }
        void WebView_JSGetPosition(object sender, JSExtInvokeArgs e)
        {
            string lat = "-25.363";
            string lng = "131.044";
            string filename = e.Arguments[0] as string;
            if (File.Exists(filename + ".txt"))
            {
                using (StreamReader file = new StreamReader(filename + ".txt"))
                {
                    lat = file.ReadLine();
                    lng = file.ReadLine();
                }
            }
            webView1.QueueScriptCall("initMap(" + lat + ", " + lng + ");");
        }
        public static string CreateObject(string targetDirectory)
        {
            string dir = "";
            string med = "";
            string objectcreated = "";
            string arraycreated = "";
            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
            foreach (string subdirectory in subdirectoryEntries)
            {
                dir = subdirectory.Replace(targetDirectory, "");
                string[] fileEntries = Directory.GetFiles(subdirectory);
                arraycreated = "[";
                foreach (string fileName in fileEntries)
                {
                    med = fileName.Replace(subdirectory, "").Replace(@"\", "");
                    if (med.EndsWith(".ytb") | med.EndsWith(".mp3") | med.EndsWith(".aac") | med.EndsWith(".wav") | med.EndsWith(".mp4") | med.EndsWith(".3gp") | med.EndsWith(".flv") | med.EndsWith(".m4a") | med.EndsWith(".ogg") | med.EndsWith(".webm") | med.EndsWith(".pdf") | med.EndsWith(".jpg") | med.EndsWith(".png") | med.EndsWith(".gif"))
                    {
                        if (!((File.Exists(fileName.Replace(".jpg", ".ytb")) | File.Exists(fileName.Replace(".jpg", ".mp3")) | File.Exists(fileName.Replace(".jpg", ".aac")) | File.Exists(fileName.Replace(".jpg", ".wav")) | File.Exists(fileName.Replace(".jpg", ".mp4")) | File.Exists(fileName.Replace(".jpg", ".3gp")) | File.Exists(fileName.Replace(".jpg", ".flv")) | File.Exists(fileName.Replace(".jpg", ".m4a")) | File.Exists(fileName.Replace(".jpg", ".ogg")) | File.Exists(fileName.Replace(".jpg", ".webm")) | File.Exists(fileName.Replace(".jpg", ".pdf"))) & med.EndsWith(".jpg")))
                        {
                            arraycreated += "'" + med + "', ";
                        }
                    }
                }
                arraycreated += "]";
                arraycreated = arraycreated.Replace(", ]", "]");
                objectcreated += "'" + dir + "': " + arraycreated + ", ";
            }
            objectcreated += "]";
            objectcreated = objectcreated.Replace(", ]", "");
            return objectcreated;
        }
        public void webView1_RequestPermissions(object sender, RequestPermissionEventArgs e)
        {
            e.Allow();
        }
        public void webView1_LoadCompleted(object sender, LoadCompletedEventArgs e)
        {
            Task.Run(() => LoadPage());
        }
        public void LoadPage()
        {
            backgroundcolor = "";
            string overlaycolor = "";
            string previousnextbuttonshovercolor = "";
            string titlehoverbackgroundcolor = "";
            using (StreamReader file = new StreamReader("colors.txt"))
            {
                file.ReadLine();
                backgroundcolor = file.ReadLine();
                file.ReadLine();
                overlaycolor = file.ReadLine();
                file.ReadLine();
                previousnextbuttonshovercolor = file.ReadLine();
                file.ReadLine();
                titlehoverbackgroundcolor = file.ReadLine();
                file.Close();
            }
            string folderpath = "file:///" + System.Reflection.Assembly.GetEntryAssembly().Location.Replace(@"file:\", "").Replace(Process.GetCurrentProcess().ProcessName + ".exe", "").Replace(@"\", "/").Replace(@"//", "") + "med/";
            string oldobject = "'Game': ['AnticheatingSolution.mp3', 'MonogameLearning.mp3'], 'Science': []";
            string newobject = CreateObject(folderpath.Replace("file:///", ""));
            string stringinject;
            stringinject = @"
    <style>

body {
    font-family: sans-serif;
    background-color: backgroundcolor;
    color: #FFFFFF;
}

.row > .col-lg-4,
.col-6 {
    padding: 0;
}

.slideshow-container {
    justify-content: center;
    display: flex;
}

.menushow-container a {
    align-items: center;
    margin: 15px;
    color: #FFFFFF;
}

.menushow-container div {
    align-items: center;
    margin: 15px;
    color: #FFFFFF;
}

.mySlides .embedmp34 {
    padding-left: 0%;
    padding-right: 0%;
    border: none;
    position: absolute;
    top: 0;
    bottom: 0;
    left: 0;
    right: 0;
    margin: auto;
}

#navbar {
    background: overlaycolor;
    position: fixed;
    top: -50px;
    width: 100%;
    height: 50px;
    display: flex;
    transition: top 0.3s;
    justify-content: center;
}

#overlay {
    position: fixed;
    overflow-y: auto;
    overflow-x: hidden;
    display: block;
    width: 100%;
    height: 35vh;
    top: 100vh;
    left: 0;
    right: 0;
    bottom: 0;
    background: overlaycolor;
    transition: top 0.3s;
}

#list {
    position: fixed;
    overflow-y: auto;
    overflow-x: hidden;
    display: none;
    top: 50px;
    width: 100%;
    height: calc(65vh - 50px);
    left: 0;
    right: 0;
    bottom: 0;
    background: overlaycolor;
    transition: top 0.3s;
}

#googlemap {
    position: fixed;
    overflow-y: auto;
    overflow-x: hidden;
    display: none;
    top: 50px;
    width: 100%;
    height: calc(65vh - 50px);
    left: 0;
    right: 0;
    bottom: 0;
    background: overlaycolor;
    transition: top 0.3s;
}

#list a {
    color: #FFFFFF;
    display: inline-block;
    margin: 20px;
}

#overlay img {
    pointer-events: none;
}

.goto, .gotochannel, .collaspse, .random {
    cursor: pointer;
    text-align: center;
    color: white;
    overflow: hidden;
}

.centered {
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    color: white;
}

.centered:hover {
    background-color: titlehoverbackgroundcolor;
}

.prev, .next {
    cursor: pointer;
    position: absolute;
    top: 50%;
    width: auto;
    padding: 16px;
    margin-top: -22px;
    color: white;
    font-weight: bold;
    font-size: 18px;
    transition: 0.6s ease;
    border-radius: 0 3px 3px 0;
    user-select: none;
    text-decoration: none;
}

.prev {
    left: 0;
    border-radius: 3px 0 0 3px;
}

.next {
    right: 0;
    border-radius: 3px 0 0 3px;
}

.prev:hover, .next:hover {
    background-color: previousnextbuttonshovercolor;
}

.thumbnailed {
    outline: 4px solid white;
    outline-offset: -4px;
}

video {
	width:60%;
	height:auto;
	top:15%;
	left:20%;
	display:block;
	position:absolute;
}

::-webkit-scrollbar {
    width: 10px;
}

::-webkit-scrollbar-track {
    background: overlaycolor;
}

::-webkit-scrollbar-thumb {
    background: #888;
}

::-webkit-scrollbar-thumb:hover {
    background: #eee;
}

audio {
  position: fixed;
  left: 150px;
  bottom: 20%;
  width: calc(100% - 300px);
}

#canvas {
  position: fixed;
  left: 150px;
  bottom: 25%;
  width: calc(100% - 300px);
  height: 75%;
}

#map {
	height: 100%;
}

    </style>
".Replace("\r\n", " ").Replace("backgroundcolor", backgroundcolor).Replace("overlaycolor", overlaycolor).Replace("previousnextbuttonshovercolor", previousnextbuttonshovercolor).Replace("titlehoverbackgroundcolor", titlehoverbackgroundcolor);
            stringinject = @"""" + stringinject + @"""";
            stringinject = @"$(" + stringinject + @" ).appendTo('head');";
            this.webView1.EvalScript(stringinject);
            stringinject = @"

    <!-- Visualizer container -->
	<canvas id=\'canvas\'></canvas>

    <!-- Slideshow container -->
    <div class='slideshow-container container-sm h-100'>
    </div>

    <!-- List container -->
    <div id='list'>
    </div>

    <!-- Google Map container -->
    <div id='googlemap'>
        <div id='map'></div>
    </div>

    <!-- Overlay container -->
    <div id='overlay'>
    </div>

    <!-- Menushow container -->
    <div class='menushow-container' id='navbar'>
    </div>

    <script>
var menuIndex = 1;
var slideIndex = 1;
var obj = { 'Game': ['AnticheatingSolution.mp3', 'MonogameLearning.mp3'], 'Science': [] };
var wd = 2;
var wu = 2;
var checkfolder = '';
var collapse = false;
var visualizeron = false;
var rand = false;
var notonoverlay = true;
var moving = false;
var player;

$(document).ready(function() {
    $.ajax({
        url: 'https://www.youtube.com/iframe_api',
        dataType: 'script'
    }).done(function() {
        getAllFilesFromFolders();
    }).fail(function() {
        getAllFilesFromFolders();
    });
});

function setEbookSource() {
    var src = $('.mediamp34:visible').attr('src'); 
    var a = document.getElementById('download');
    a.href = src;
}

function setEbookPlayOverlay() {
    try {
        var element = document.getElementsByClassName('thumbnailed');
        for (var i = 0; i < element.length; i++) {
            element[i].style.width = '100%';
            element[i].style.height = '100%'; 
        }
        $('img').removeClass('thumbnailed');
        var id = $('.mediamp34:visible').attr('id');
        var el = document.getElementById(id + '-Overlay');
        el.classList.add('thumbnailed');
        var elements = document.getElementsByClassName('overlaytitle');
        for (var i = 0; i < elements.length; i++) {
            elements[i].style.cssText = 'height:7vh;color:#FFFFFF;';
        }
        var e = document.getElementById(id + '-OverlayTitle');
        e.style.cssText = 'height:7vh;color:#FFFFFF;font-style:italic;';
    }
    catch {
        return;
    }
}

function plusSlides(n) {
    showSlides(slideIndex += n);
}

function showSlides(n) {
  var folder = $('.folder:visible').text();
  var files = obj[folder];
  if (n > files.length) {
    slideIndex = 1;
  }    
  if (n < 1) {
    slideIndex = files.length;
  }
  if (rand) {
      var rndnext = files.length;
      var rnd = Math.floor(Math.random() * rndnext) + 1;
      slideIndex = rnd;
  }
  clickMenu(slideIndex-1);
}

function plusMenu(n) {
    showMenu(menuIndex += n);
}

function showMenu(n) {
    var menu = document.getElementsByClassName('myMenu');
    if (n > menu.length) {
        menuIndex = 1;
    }
    if (n < 1) {
        menuIndex = menu.length;
    }
    for (var i = 0; i < menu.length; i++) {
        menu[i].style.display = 'none';
    }
    menu[menuIndex-1].style.display = 'block';
    setTimeout((function(){
        document.getElementById('navbar').style.top = '0';
        document.getElementById('overlay').style.top = '65vh';
        createOverlay();
        setEbookPlayOverlay();
    })
    , 500);
}

function getAllFilesFromFolders() {
    createMenu();
    showSlides(1);
}

function createMenu() {
    var keyNames = Object.keys(obj);
    let htmlString = '';
    htmlString = `<div class=\'bg-light collaspse\' style=\'display:float;position:absolute;float:left;left:10px;\' onclick=\'listCollaspse();\'>
                    <i class=\'fa fa-bars\'></i></div>
                    <a href='#' onclick=\'plusMenu(-1)\' style=\'text-decoration:none;\'><</a>`;
    for (let keyName of keyNames) {
    	htmlString += 
        `<div class=\'myMenu\'>
            <a href='#' onclick=\'clickMenu(0)\' class=\'folder\' style=\'text-decoration:none;\'>`+ keyName +`</a>
        </div>`;
    }
    htmlString += `<a href='#' onclick=\'plusMenu(1)\' style=\'text-decoration:none;\'>></a>
                    <div class=\'bg-light random\' style=\'display:float;position:absolute;float:right;right:70px;color:gray;\' onclick=\'random();\' title=\'set random playing\'>
                    <i class=\'fa fa-random\'></i></div>
                    <a href='#' onclick=\'openMap()\' class=\'bg-light\' style=\'display:float;position:absolute;float:right;right:40px;\'>
                    <i class=\'fa fa-map-marker\'></i></a>
                    <a href=\'\' target='_blank' class=\'bg-light\' style=\'display:float;position:absolute;float:right;right:10px;\' id=\'download\' download>
                    <i class=\'fa fa-download\'></i></a>`;
    $('.menushow-container').append(htmlString);
    var folders = (Object.keys(obj).map(key => key));
    var index = 0;
    htmlString = ``;
    for (let folder of folders) {
    	    htmlString += `<a onclick=\'goToChannel(this)\' data-folder=\'` + index + `\' class=\'gotochannel\'>` + folder + `</a>`;
            index++;
    }
    $('#list').append(htmlString);
    showMenu(1);
}

function clickMenu(fileindex) {
    var folder = $('.folder:visible').text();
    var files = obj[folder];
    $('.slideshow-container').html('');
    let htmlString = ``;
    var inc = 0;
    for (let file of files) {
        if (fileindex == inc) {
            if (file.includes('.ytb')) {
               var videoid = file.substring(file.lastIndexOf('-ytbembed-') + 10, file.lastIndexOf('.ytb'));
               var ytbsrc =  'https://www.youtube.com/watch?v=' + videoid;
               var a = document.getElementById('download');
               a.href = ytbsrc;
               ytbsrc = ytbsrc.replace('https://www.youtube.com/watch?v=', 'https://www.youtube.com/embed/') + '?enablejsapi=1';
    	       htmlString += `<div class=\'mySlides\' align=\'center\'>
                           <div class=\'item\'>
                                <iframe src=\'` + ytbsrc + `\' frameborder=\'0\' allowfullscreen class=\'mediamp34 embedmp34\' id=\'` + folder + `-` + file + `\' data-name=\'` + file + `\' style=\'width:800px;height:450px;\' allow='autoplay'></iframe>
                           </div>
                           <div class=\'centered\' style=\'top:97%;align-items:center;position:absolute;\'>` + folder + ` : ` + file + `</div>
                           </div>`;
                visualizeron = false;
            }
            if (file.includes('.mp3') | file.includes('.aac') | file.includes('.wav')) {
    	       htmlString += `<div class=\'mySlides\' align=\'center\'>
                            <div class=\'item\'>
			                    <audio controls class=\'mediamp34\' id=\'` + folder + `-` + file + `\' data-name=\'` + file + `\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + file + `\' onended=\'mediaEnded()\'></audio>
                             </div>
                           <div class=\'centered\' style=\'top:97%;align-items:center;position:absolute;\'>` + folder + ` : ` + file + `</div>
                           </div>`;
                visualizeron = true;
            }
            if (file.includes('.mp4') | file.includes('.3gp') | file.includes('.flv') | file.includes('.m4a') | file.includes('.ogg') | file.includes('.webm')) {
    	        htmlString += `<div class=\'mySlides\' align=\'center\'>
                            <div class=\'item\'>
			                    <video controls width=\'800\' height=\'300\' class=\'mediamp34\' id=\'` + folder + `-` + file + `\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + file + `\' onended=\'mediaEnded()\'></video>
                             </div>
                           <div class=\'centered\' style=\'top:97%;align-items:center;position:absolute;\'>` + folder + ` : ` + file + `</div>
                           </div>`;
                visualizeron = false;
            }
            if (file.includes('.pdf')) {
    	        htmlString += `<div class=\'mySlides\' align=\'center\'>
                            <div class=\'item\'>
			                    <iframe class=\'mediamp34\' id=\'` + folder + `-` + file + `\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + file + `\' style=\'width:90%;height:90%;top:5%;left:5%;display:block;position:absolute;\'></iframe>
                            </div>
                           <div class=\'centered\' style=\'top:97%;align-items:center;position:absolute;\'>` + folder + ` : ` + file + `</div>
                           </div>`;
                visualizeron = false;
            }
            if (file.includes('.jpg') | file.includes('.png') | file.includes('.gif')) {
    	        htmlString += `<div class=\'mySlides\' align=\'center\'>
                            <div class=\'item\'>
			                    <img class=\'mediamp34\' id=\'` + folder + `-` + file + `\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + file + `\' style=\'width:60%;height:auto;top:15%;left:20%;display:block;position:absolute;\'></img>
                            </div>
                           <div class=\'centered\' style=\'top:97%;align-items:center;position:absolute;\'>` + folder + ` : ` + file + `</div>
                           </div>`;
                visualizeron = false;
            }
            break;
        }
        inc++;
    }
    htmlString += `<div>
                    <a class=\'prev\' onclick=\'plusSlides(-1)\' style=\'text-decoration:none;color:white;\'>&#10094;</a>
                    <a class=\'next\' onclick=\'plusSlides(1)\' style=\'text-decoration:none;color:white;\'>&#10095;</a>
                   </div>`;
    $('.slideshow-container').html(htmlString);
    slideIndex = fileindex + 1;
    setEbookSource(); 
    setEbookPlayOverlay();
    mediaAutoPlay();
}

function createOverlay() {
    var folder = $('.folder:visible').text();
    var folderindex = (Object.keys(obj).map(key => key)).indexOf(folder);
    var fileindex = 0;
    if (folder != checkfolder) {
        checkfolder = folder;
        var files = obj[folder];
        let htmlString = `<div class=\'container\'>
                             <div class=\'row row-eq-height\'>`;
        for (let file of files) {
            var str = file.replace('.ytb', '.jpg');
            str = str.replace('.mp3', '.jpg');
            str = str.replace('.aac', '.jpg');
            str = str.replace('.wav', '.jpg');
            str = str.replace('.mp4', '.jpg');
            str = str.replace('.3gp', '.jpg');
            str = str.replace('.flv', '.jpg');
            str = str.replace('.m4a', '.jpg');
            str = str.replace('.ogg', '.jpg');
            str = str.replace('.webm', '.jpg');
            str = str.replace('.pdf', '.jpg');
    	    htmlString += `<a onclick=\'goToEbook(this)\' data-folderindex=\'` + folderindex + `\' data-fileindex=\'` + fileindex + `\' class=\'goto\'>
                             <div class=\'col-xs-3\' style=\'margin-top:3vh;\'>
                                  <img class=\'align-middle\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + str + `\' id=\'` + folder + `-` + file + `-Overlay\' style=\'height:100%;width:100%;\' />
                               <div class=\'text-center align-middle overlaytitle\' id=\'` + folder + `-` + file + `-OverlayTitle\' style=\'height:7vh;color:#FFFFFF;\'>` + file + `</div>
                             </div>
                           </a>`;
             fileindex++;
        }
        htmlString += `</div>
                      </div>`;
        $('#overlay').html(htmlString);
        document.getElementById('overlay').scrollTop = '0px';
    }
}

function mediaAutoPlay() {
    try {
        var mediaelements = document.getElementsByClassName('mediamp34');
        for (var i = 0; i < mediaelements.length; i++) {
            try {
                mediaelements[i].pause();
            }
            catch {}
        }
        var mediaelement = $('.mediamp34:visible')[0];
        mediaelement.play();
    }
    catch {}
    try {
        var embedelements = document.getElementsByClassName('embedmp34');
        for (var i = 0; i < embedelements.length; i++) {
            try {
                var srcs = embedelements[i].src;
                srcs = srcs.replace('&autoplay=1', '');
                embedelements[i].src = srcs;
            }
            catch {}
        }
        var embedelement = $('.embedmp34:visible')[0];
        embedelement.src += '&autoplay=1';
        var id = embedelement.id;
        window.YT.ready(function() { 
            player[id] = new YT.Player(id, {
                events: { 'onStateChange': onPlayerStateChange }
            });
        });
    }
    catch {}
}

function mediaEnded() {
    slideIndex += 1;
    showSlides(slideIndex);
}

function onPlayerStateChange(event) {
    if (event.data == YT.PlayerState.ENDED) {
        mediaEnded();
    }
}

function random() {
    var element = document.getElementsByClassName('random');
    if (!rand) {
        rand = true;
        for (var i = 0; i < element.length; i++) {
            element[i].style.color = 'white';
        }
    }
    else {
        rand = false;
        for (var i = 0; i < element.length; i++) {
            element[i].style.color = 'gray';
        }
    }
}

function goToEbook(el) {
    if (rand) {
        random();
    }
    var fileindex = Number(el.dataset.fileindex);
    slideIndex = 1;
    slideIndex += fileindex;
    showSlides(slideIndex);
}

function goToChannel(el) {
    var folderindex = Number(el.dataset.folder);
    menuIndex = folderindex + 1;
    showMenu(menuIndex);
}

$(document).on('mousemove', function(event) {
    mouseOnTop(event.pageY);
});

$('#navbar').bind('mouseleave', function(e){
    notonoverlay = true;
});

$('#navbar').bind('mouseenter', function(e){
    notonoverlay = false;
});

$('#overlay').bind('mouseleave', function(e){
    notonoverlay = true;
});

$('#overlay').bind('mouseenter', function(e){
    notonoverlay = false;
});

$('.item').bind('mouseleave', function(e){
    notonoverlay = false;
    mouseOnTop(250);
});

$('.item').bind('mouseenter', function(e){
    notonoverlay = true;
    mouseOnTop(250);
});

var oldpositiony = 0;
var newpositiony = 0;
var mouseontopy = 0;

setInterval(function() { 
    oldpositiony = newpositiony;
    newpositiony = mouseontopy;
    if (oldpositiony != newpositiony) {
        moving = true;
    }
    else {
        moving = false;
        mouseOnTop(250);
    }
}, 1000);

function mouseOnTop(y) {
    mouseontopy = y;
    if (wd == 1) {
        document.getElementById('navbar').style.top = '0';
        document.getElementById('overlay').style.top = '65vh';
    }
    if (wu == 1 & !collapse) {
        document.getElementById('navbar').style.top = '-50px';
        document.getElementById('overlay').style.top = '100vh';
    }
    var windowsizey = $(window).innerHeight();
    if (((y < 50 | y > windowsizey * 80 / 100 + 6) | !notonoverlay) & moving)
    {
	    if (wd <= 1) {
		    wd = wd + 1;
	    }
	    wu = 0;
    }
    else
    {
	    if (wu <= 1) {
		    wu = wu + 1;
	    }
	    wd = 0;
    }
}

function listCollaspse() {
    if (!collapse) {
        collapse = true;
        document.getElementById('list').style.display = 'inline-block';
        document.getElementById('googlemap').style.display = 'none';
    }
    else {
        collapse = false;
        document.getElementById('list').style.display = 'none';
    }
}

function openMap() {
    if (!collapse) {
        collapse = true;
        document.getElementById('googlemap').style.display = 'inline-block';
        document.getElementById('list').style.display = 'none';
        var a = document.getElementById('download');
        var src = a.href;
        var str = src.replace('.ytb', '.jpg');
        str = str.replace('.mp3', '');
        str = str.replace('.aac', '');
        str = str.replace('.wav', '');
        str = str.replace('.mp4', '');
        str = str.replace('.3gp', '');
        str = str.replace('.flv', '');
        str = str.replace('.m4a', '');
        str = str.replace('.ogg', '');
        str = str.replace('.webm', '');
        str = str.replace('.pdf', '');
        str = str.replace('.png', '');
        str = str.replace('.jpg', '');
        str = str.replace('.gif', '');
        str = str.replace('file:///', '');
        getPosition(str);
    }
    else {
        collapse = false;
        document.getElementById('googlemap').style.display = 'none';
    }
}

    </script>

    <script>
	var marker;
	var map;
	var uluru;
    var latstart = -25.363;
    var lngstart = 131.044;

	initMap(latstart, lngstart);

	function initMap(lat, lng) {

	  uluru = { lat: parseFloat(lat), lng: parseFloat(lng) };

	  map = new google.maps.Map(document.getElementById('map'), {
	    zoom: 4,
	    center: uluru
	  });

	  marker = new google.maps.Marker({
	    position: uluru,
	    map
	  });
 
		map.addListener('click', function (event) {
			displayCoordinates(event.latLng);               
		});
	}

function displayCoordinates(pnt) {
	var confirmed =	confirm('Are you sure to place the position here ?');
	if (confirmed) {
	    var lat = pnt.lat();
	    lat = lat.toFixed(4);
	    var lng = pnt.lng();
	    lng = lng.toFixed(4);
        var a = document.getElementById('download');
        var src = a.href;
        var str = src.replace('.ytb', '.jpg');
        str = str.replace('.mp3', '');
        str = str.replace('.aac', '');
        str = str.replace('.wav', '');
        str = str.replace('.mp4', '');
        str = str.replace('.3gp', '');
        str = str.replace('.flv', '');
        str = str.replace('.m4a', '');
        str = str.replace('.ogg', '');
        str = str.replace('.webm', '');
        str = str.replace('.pdf', '');
        str = str.replace('.png', '');
        str = str.replace('.jpg', '');
        str = str.replace('.gif', '');
        str = str.replace('file:///', '');
        savePosition(lat.toString(), lng.toString(), str);
	    var position = new google.maps.LatLng(lat, lng);
	    marker.setPosition(position);
	}
}
     </script>

    <script>

var smoothred = [];
var smoothgreen = [];
var smoothblue = [];
var coefficientred;
var coefficientgreen;
var coefficientblue;
const average = (array) => array.reduce((a, b) => a + b) / array.length;
var ctx;
var WIDTH;
var HEIGHT;
var barWidth;
var barHeight;
var x;    
var canvas;

getCanevas();

function getCanevas() {
      canvas = document.getElementById('canvas');
      ctx = canvas.getContext('2d');
      WIDTH = canvas.width;
      HEIGHT = canvas.height;
}

    </script>

    <script src='https://maps.googleapis.com/maps/api/js?key=AIzaSyA2cr6DUhxG8bjt8FeM6SUoj_dG9VQTi8M&callback=initMap&v=weekly&channel=2' async></script>

".Replace("\r\n", " ").Replace("file:///C:/Users/mic/Documents/GitHub/playmedia/med/", folderpath).Replace(oldobject, newobject).Replace("backgroundcolor", backgroundcolor);
            stringinject = @"""" + stringinject + @"""";
            stringinject = @"$(document).ready(function(){$('body').append(" + stringinject + @");});";
            this.webView1.EvalScript(stringinject);
            GetAudioByteArray();
            Task.Run(() => Start());
        }
        public void Navigate(string address)
        {
            if (String.IsNullOrEmpty(address))
                return;
            if (address.Equals("about:blank"))
                return;
            if (!address.StartsWith("http://") & !address.StartsWith("https://"))
                address = "https://" + address;
            try
            {
                webView1.Url = address;
            }
            catch (System.UriFormatException)
            {
                return;
            }
        }
        public void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            closed = true;
            capture.Stop();
            this.webView1.Dispose();
        }
    }
}
namespace WinformsVisualization.Visualization
{
    /// <summary>
    ///     BasicSpectrumProvider
    /// </summary>
    public class BasicSpectrumProvider : FftProvider, ISpectrumProvider
    {
        public readonly int _sampleRate;
        public readonly List<object> _contexts = new List<object>();

        public BasicSpectrumProvider(int channels, int sampleRate, FftSize fftSize)
            : base(channels, fftSize)
        {
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException("sampleRate");
            _sampleRate = sampleRate;
        }

        public int GetFftBandIndex(float frequency)
        {
            int fftSize = (int)FftSize;
            double f = _sampleRate / 2.0;
            // ReSharper disable once PossibleLossOfFraction
            return (int)((frequency / f) * (fftSize / 2));
        }

        public bool GetFftData(float[] fftResultBuffer, object context)
        {
            if (_contexts.Contains(context))
                return false;

            _contexts.Add(context);
            GetFftData(fftResultBuffer);
            return true;
        }

        public override void Add(float[] samples, int count)
        {
            base.Add(samples, count);
            if (count > 0)
                _contexts.Clear();
        }

        public override void Add(float left, float right)
        {
            base.Add(left, right);
            _contexts.Clear();
        }
    }
}
namespace WinformsVisualization.Visualization
{
    public interface ISpectrumProvider
    {
        bool GetFftData(float[] fftBuffer, object context);
        int GetFftBandIndex(float frequency);
    }
}
namespace WinformsVisualization.Visualization
{
    internal class GradientCalculator
    {
        public Color[] _colors;

        public GradientCalculator()
        {
        }

        public GradientCalculator(params Color[] colors)
        {
            _colors = colors;
        }

        public Color[] Colors
        {
            get { return _colors ?? (_colors = new Color[] { }); }
            set { _colors = value; }
        }

        public Color GetColor(float perc)
        {
            if (_colors.Length > 1)
            {
                int index = Convert.ToInt32((_colors.Length - 1) * perc - 0.5f);
                float upperIntensity = (perc % (1f / (_colors.Length - 1))) * (_colors.Length - 1);
                if (index + 1 >= Colors.Length)
                    index = Colors.Length - 2;

                return Color.FromArgb(
                    255,
                    (byte)(_colors[index + 1].R * upperIntensity + _colors[index].R * (1f - upperIntensity)),
                    (byte)(_colors[index + 1].G * upperIntensity + _colors[index].G * (1f - upperIntensity)),
                    (byte)(_colors[index + 1].B * upperIntensity + _colors[index].B * (1f - upperIntensity)));
            }
            return _colors.FirstOrDefault();
        }
    }
}
namespace WinformsVisualization.Visualization
{
    public class LineSpectrum : SpectrumBase
    {
        public int _barCount;
        public double _barSpacing;
        public double _barWidth;
        public Size _currentSize;

        public LineSpectrum(FftSize fftSize)
        {
            FftSize = fftSize;
        }

        [Browsable(false)]
        public double BarWidth
        {
            get { return _barWidth; }
        }

        public double BarSpacing
        {
            get { return _barSpacing; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value");
                _barSpacing = value;
                UpdateFrequencyMapping();

                RaisePropertyChanged("BarSpacing");
                RaisePropertyChanged("BarWidth");
            }
        }

        public int BarCount
        {
            get { return _barCount; }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value");
                _barCount = value;
                SpectrumResolution = value;
                UpdateFrequencyMapping();

                RaisePropertyChanged("BarCount");
                RaisePropertyChanged("BarWidth");
            }
        }

        [BrowsableAttribute(false)]
        public Size CurrentSize
        {
            get { return _currentSize; }
            set
            {
                _currentSize = value;
                RaisePropertyChanged("CurrentSize");
            }
        }

        public Bitmap CreateSpectrumLine(Size size, Brush brush, Color background, bool highQuality)
        {
            if (!UpdateFrequencyMappingIfNessesary(size))
                return null;

            var fftBuffer = new float[(int)FftSize];

            //get the fft result from the spectrum provider
            if (SpectrumProvider.GetFftData(fftBuffer, this))
            {
                using (var pen = new Pen(brush, (float)_barWidth))
                {
                    var bitmap = new Bitmap(size.Width, size.Height);

                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        PrepareGraphics(graphics, highQuality);
                        graphics.Clear(background);

                        CreateSpectrumLineInternal(graphics, pen, fftBuffer, size);
                    }

                    return bitmap;
                }
            }
            return null;
        }

        public Bitmap CreateSpectrumLine(Size size, Color color1, Color color2, Color background, bool highQuality)
        {
            if (!UpdateFrequencyMappingIfNessesary(size))
                return null;

            using (
                Brush brush = new LinearGradientBrush(new RectangleF(0, 0, (float)_barWidth, size.Height), color2,
                    color1, LinearGradientMode.Vertical))
            {
                return CreateSpectrumLine(size, brush, background, highQuality);
            }
        }

        public void CreateSpectrumLineInternal(Graphics graphics, Pen pen, float[] fftBuffer, Size size)
        {
            int height = size.Height;
            //prepare the fft result for rendering 
            SpectrumPointData[] spectrumPoints = CalculateSpectrumPoints(height, fftBuffer);

            //connect the calculated points with lines
            for (int i = 0; i < spectrumPoints.Length; i++)
            {
                SpectrumPointData p = spectrumPoints[i];
                int barIndex = p.SpectrumPointIndex;
                double xCoord = BarSpacing * (barIndex + 1) + (_barWidth * barIndex) + _barWidth / 2;

                var p1 = new PointF((float)xCoord, height);
                var p2 = new PointF((float)xCoord, height - (float)p.Value - 1);

                graphics.DrawLine(pen, p1, p2);
            }
        }

        public override void UpdateFrequencyMapping()
        {
            _barWidth = Math.Max(((_currentSize.Width - (BarSpacing * (BarCount + 1))) / BarCount), 0.00001);
            base.UpdateFrequencyMapping();
        }

        public bool UpdateFrequencyMappingIfNessesary(Size newSize)
        {
            if (newSize != CurrentSize)
            {
                CurrentSize = newSize;
                UpdateFrequencyMapping();
            }

            return newSize.Width > 0 && newSize.Height > 0;
        }

        public void PrepareGraphics(Graphics graphics, bool highQuality)
        {
            if (highQuality)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.CompositingQuality = CompositingQuality.AssumeLinear;
                graphics.PixelOffsetMode = PixelOffsetMode.Default;
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            }
            else
            {
                graphics.SmoothingMode = SmoothingMode.HighSpeed;
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.PixelOffsetMode = PixelOffsetMode.None;
                graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            }
        }
        public float[] GetSpectrumPoints(float height, float[] fftBuffer)
        {
            SpectrumPointData[] dats = CalculateSpectrumPoints(height, fftBuffer);
            float[] res = new float[dats.Length];
            for (int i = 0; i < dats.Length; i++)
            {
                res[i] = (float)dats[i].Value;
            }

            return res;
        }
    }
}
namespace WinformsVisualization.Visualization
{
    public class SpectrumBase : INotifyPropertyChanged
    {
        public const int ScaleFactorLinear = 9;
        public const int ScaleFactorSqr = 2;
        public const double MinDbValue = -90;
        public const double MaxDbValue = 0;
        public const double DbScale = (MaxDbValue - MinDbValue);

        public int _fftSize;
        public bool _isXLogScale;
        public int _maxFftIndex;
        public int _maximumFrequency = 20000;
        public int _maximumFrequencyIndex;
        public int _minimumFrequency = 20; //Default spectrum from 20Hz to 20kHz
        public int _minimumFrequencyIndex;
        public ScalingStrategy _scalingStrategy;
        public int[] _spectrumIndexMax;
        public int[] _spectrumLogScaleIndexMax;
        public ISpectrumProvider _spectrumProvider;

        public int SpectrumResolution;
        public bool _useAverage;

        public int MaximumFrequency
        {
            get { return _maximumFrequency; }
            set
            {
                if (value <= MinimumFrequency)
                {
                    throw new ArgumentOutOfRangeException("value",
                        "Value must not be less or equal the MinimumFrequency.");
                }
                _maximumFrequency = value;
                UpdateFrequencyMapping();

                RaisePropertyChanged("MaximumFrequency");
            }
        }

        public int MinimumFrequency
        {
            get { return _minimumFrequency; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value");
                _minimumFrequency = value;
                UpdateFrequencyMapping();

                RaisePropertyChanged("MinimumFrequency");
            }
        }

        [BrowsableAttribute(false)]
        public ISpectrumProvider SpectrumProvider
        {
            get { return _spectrumProvider; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");
                _spectrumProvider = value;

                RaisePropertyChanged("SpectrumProvider");
            }
        }

        public bool IsXLogScale
        {
            get { return _isXLogScale; }
            set
            {
                _isXLogScale = value;
                UpdateFrequencyMapping();
                RaisePropertyChanged("IsXLogScale");
            }
        }

        public ScalingStrategy ScalingStrategy
        {
            get { return _scalingStrategy; }
            set
            {
                _scalingStrategy = value;
                RaisePropertyChanged("ScalingStrategy");
            }
        }

        public bool UseAverage
        {
            get { return _useAverage; }
            set
            {
                _useAverage = value;
                RaisePropertyChanged("UseAverage");
            }
        }

        [BrowsableAttribute(false)]
        public FftSize FftSize
        {
            get { return (FftSize)_fftSize; }
            set
            {
                if ((int)Math.Log((int)value, 2) % 1 != 0)
                    throw new ArgumentOutOfRangeException("value");

                _fftSize = (int)value;
                _maxFftIndex = _fftSize / 2 - 1;

                RaisePropertyChanged("FFTSize");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void UpdateFrequencyMapping()
        {
            _maximumFrequencyIndex = Math.Min(_spectrumProvider.GetFftBandIndex(MaximumFrequency) + 1, _maxFftIndex);
            _minimumFrequencyIndex = Math.Min(_spectrumProvider.GetFftBandIndex(MinimumFrequency), _maxFftIndex);

            int actualResolution = SpectrumResolution;

            int indexCount = _maximumFrequencyIndex - _minimumFrequencyIndex;
            double linearIndexBucketSize = Math.Round(indexCount / (double)actualResolution, 3);

            _spectrumIndexMax = _spectrumIndexMax.CheckBuffer(actualResolution, true);
            _spectrumLogScaleIndexMax = _spectrumLogScaleIndexMax.CheckBuffer(actualResolution, true);

            double maxLog = Math.Log(actualResolution, actualResolution);
            for (int i = 1; i < actualResolution; i++)
            {
                int logIndex =
                    (int)((maxLog - Math.Log((actualResolution + 1) - i, (actualResolution + 1))) * indexCount) +
                    _minimumFrequencyIndex;

                _spectrumIndexMax[i - 1] = _minimumFrequencyIndex + (int)(i * linearIndexBucketSize);
                _spectrumLogScaleIndexMax[i - 1] = logIndex;
            }

            if (actualResolution > 0)
            {
                _spectrumIndexMax[_spectrumIndexMax.Length - 1] =
                    _spectrumLogScaleIndexMax[_spectrumLogScaleIndexMax.Length - 1] = _maximumFrequencyIndex;
            }
        }

        public virtual SpectrumPointData[] CalculateSpectrumPoints(double maxValue, float[] fftBuffer)
        {
            var dataPoints = new List<SpectrumPointData>();

            double value0 = 0, value = 0;
            double lastValue = 0;
            double actualMaxValue = maxValue;
            int spectrumPointIndex = 0;

            for (int i = _minimumFrequencyIndex; i <= _maximumFrequencyIndex; i++)
            {
                switch (ScalingStrategy)
                {
                    case ScalingStrategy.Decibel:
                        value0 = (((20 * Math.Log10(fftBuffer[i])) - MinDbValue) / DbScale) * actualMaxValue;
                        break;
                    case ScalingStrategy.Linear:
                        value0 = (fftBuffer[i] * ScaleFactorLinear) * actualMaxValue;
                        break;
                    case ScalingStrategy.Sqrt:
                        value0 = ((Math.Sqrt(fftBuffer[i])) * ScaleFactorSqr) * actualMaxValue;
                        break;
                }

                bool recalc = true;

                value = Math.Max(0, Math.Max(value0, value));

                while (spectrumPointIndex <= _spectrumIndexMax.Length - 1 &&
                       i ==
                       (IsXLogScale
                           ? _spectrumLogScaleIndexMax[spectrumPointIndex]
                           : _spectrumIndexMax[spectrumPointIndex]))
                {
                    if (!recalc)
                        value = lastValue;

                    if (value > maxValue)
                        value = maxValue;

                    if (_useAverage && spectrumPointIndex > 0)
                        value = (lastValue + value) / 2.0;

                    dataPoints.Add(new SpectrumPointData { SpectrumPointIndex = spectrumPointIndex, Value = value });

                    lastValue = value;
                    value = 0.0;
                    spectrumPointIndex++;
                    recalc = false;
                }

                //value = 0;
            }

            return dataPoints.ToArray();
        }

        public void RaisePropertyChanged(string propertyName)
        {
            if (PropertyChanged != null && !String.IsNullOrEmpty(propertyName))
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        [DebuggerDisplay("{Value}")]
        public struct SpectrumPointData
        {
            public int SpectrumPointIndex;
            public double Value;
        }
    }
}
namespace WinformsVisualization.Visualization
{
    public enum ScalingStrategy
    {
        Decibel,
        Linear,
        Sqrt
    }
}