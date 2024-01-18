using System;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Drawing;
using System.ComponentModel;
using CSCore.Streams;
using CSCore.SoundIn;
using CSCore;
using CSCore.DSP;
using WinformsVisualization.Visualization;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using WebView2 = Microsoft.Web.WebView2.WinForms.WebView2;

namespace Playmedia
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        public int numBars = 100;
        public float[] barData = new float[100];
        public int minFreq = 0;
        public int maxFreq = 23000;
        public int barSpacing = 0;
        public bool logScale = true;
        public bool isAverage = false;
        public float highScaleAverage = 1400f;
        public float highScaleNotAverage = 1400f;
        public LineSpectrum lineSpectrum;
        public WasapiCapture capture;
        public FftSize fftSize;
        public float[] fftBuffer;
        public BasicSpectrumProvider spectrumProvider;
        public IWaveSource finalSource;
        public static string backgroundcolor = "";
        public static string frequencystickscolor = "";
        public static bool closed = false;
        public static WebView2 webView21 = new WebView2();
        public async void Form1_Shown(object sender, EventArgs e)
        {
            CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions("--disable-web-security --allow-file-access-from-files --allow-file-access", "en");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, null, options);
            await webView21.EnsureCoreWebView2Async(environment);
            webView21.CoreWebView2.ContainsFullScreenElementChanged += (obj, args) =>
            {
                this.FullScreen = webView21.CoreWebView2.ContainsFullScreenElement;
            };
            webView21.CoreWebView2.SetVirtualHostNameToFolderMapping("appassets", "assets", CoreWebView2HostResourceAccessKind.DenyCors);
            webView21.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView21.CoreWebView2.AddHostObjectToScript("bridge", new Bridge());
            webView21.NavigationCompleted += WebView21_NavigationCompleted;
            string folderpath = "file:///" + System.Reflection.Assembly.GetEntryAssembly().Location.Replace(@"file:\", "").Replace(Process.GetCurrentProcess().ProcessName + ".exe", "").Replace(@"\", "/").Replace(@"//", "");
            string path = @"playmedia.html";
            string readText = File.ReadAllText(path);
            webView21.NavigateToString(readText);
            webView21.Dock = DockStyle.Fill;
            this.Controls.Add(webView21);
            GetAudioByteArray();
        }
        private bool fullScreen = false;
        [DefaultValue(false)]
        public bool FullScreen
        {
            get { return fullScreen; }
            set
            {
                fullScreen = value;
                if (value)
                {
                    this.WindowState = FormWindowState.Normal;
                    FormBorderStyle = FormBorderStyle.None;
                    WindowState = FormWindowState.Maximized;
                }
                else
                {
                    this.Activate();
                    this.FormBorderStyle = FormBorderStyle.Sizable;
                    this.WindowState = FormWindowState.Normal;
                }
            }
        }
        private void HandlePermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
            {
                e.State = CoreWebView2PermissionState.Allow;
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
        private async void timer1_Tick(object sender, EventArgs e)
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
                            var audiorawdata = [rawdata100];
                            barWidth = WIDTH / 99;
                            barHeight = HEIGHT;
                            x = 0;
                            for (var i = 1; i < 100; i += 1) {
                                barHeight = audiorawdata[i];
                                ctx.fillStyle = 'frequencystickscolor';
                                ctx.strokeStyle = 'frequencystickscolor';
                                ctx.fillRect(x, HEIGHT - barHeight, barWidth, barHeight);
                                x += barWidth;
                            }
                            ctx.stroke();
                        }
                        catch {}
                    ";
                    await execScriptHelper(stringinject.Replace("backgroundcolor", backgroundcolor).Replace("frequencystickscolor", frequencystickscolor).Replace("rawdata100", (int)barData[0] + ", " + (int)barData[1] + ", " + (int)barData[2] + ", " + (int)barData[3] + ", " + (int)barData[4] + ", " + (int)barData[5] + ", " + (int)barData[6] + ", " + (int)barData[7] + ", " + (int)barData[8] + ", " + (int)barData[9] + ", " + (int)barData[10] + ", " + (int)barData[11] + ", " + (int)barData[12] + ", " + (int)barData[13] + ", " + (int)barData[14] + ", " + (int)barData[15] + ", " + (int)barData[16] + ", " + (int)barData[17] + ", " + (int)barData[18] + ", " + (int)barData[19] + ", " + (int)barData[20] + ", " + (int)barData[21] + ", " + (int)barData[22] + ", " + (int)barData[23] + ", " + (int)barData[24] + ", " + (int)barData[25] + ", " + (int)barData[26] + ", " + (int)barData[27] + ", " + (int)barData[28] + ", " + (int)barData[29] + ", " + (int)barData[30] + ", " + (int)barData[31] + ", " + (int)barData[32] + ", " + (int)barData[33] + ", " + (int)barData[34] + ", " + (int)barData[35] + ", " + (int)barData[36] + ", " + (int)barData[37] + ", " + (int)barData[38] + ", " + (int)barData[39] + ", " + (int)barData[40] + ", " + (int)barData[41] + ", " + (int)barData[42] + ", " + (int)barData[43] + ", " + (int)barData[44] + ", " + (int)barData[45] + ", " + (int)barData[46] + ", " + (int)barData[47] + ", " + (int)barData[48] + ", " + (int)barData[49] + ", " + (int)barData[50] + ", " + (int)barData[51] + ", " + (int)barData[52] + ", " + (int)barData[53] + ", " + (int)barData[54] + ", " + (int)barData[55] + ", " + (int)barData[56] + ", " + (int)barData[57] + ", " + (int)barData[58] + ", " + (int)barData[59] + ", " + (int)barData[60] + ", " + (int)barData[61] + ", " + (int)barData[62] + ", " + (int)barData[63] + ", " + (int)barData[64] + ", " + (int)barData[65] + ", " + (int)barData[66] + ", " + (int)barData[67] + ", " + (int)barData[68] + ", " + (int)barData[69] + ", " + (int)barData[70] + ", " + (int)barData[71] + ", " + (int)barData[72] + ", " + (int)barData[73] + ", " + (int)barData[74] + ", " + (int)barData[75] + ", " + (int)barData[76] + ", " + (int)barData[77] + ", " + (int)barData[78] + ", " + (int)barData[79] + ", " + (int)barData[80] + ", " + (int)barData[81] + ", " + (int)barData[82] + ", " + (int)barData[83] + ", " + (int)barData[84] + ", " + (int)barData[85] + ", " + (int)barData[86] + ", " + (int)barData[87] + ", " + (int)barData[88] + ", " + (int)barData[89] + ", " + (int)barData[90] + ", " + (int)barData[91] + ", " + (int)barData[92] + ", " + (int)barData[93] + ", " + (int)barData[94] + ", " + (int)barData[95] + ", " + (int)barData[96] + ", " + (int)barData[97] + ", " + (int)barData[98] + ", " + (int)barData[99]));
                }
                catch { }
            }
        }
        private void WebView21_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            LoadPage();
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
        public async void LoadPage()
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
                file.ReadLine();
                frequencystickscolor = file.ReadLine();
                file.Close();
            }
            string folderpath = "file:///" + System.Reflection.Assembly.GetEntryAssembly().Location.Replace(@"file:\", "").Replace(Process.GetCurrentProcess().ProcessName + ".exe", "").Replace(@"\", "/").Replace(@"//", "") + "/assets/med/";
            string oldobject = "'Game': ['AnticheatingSolution.mp3', 'MonogameLearning.mp3'], 'Science': []";
            string newobject = CreateObject(folderpath.Replace("file:///", ""));
            string stringinject = @"
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
    top:50%;
    left:50%;
    margin:auto;
    transform:translate(-50%,-50%);
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
  bottom: 45%;
  width: calc(100% - 300px);
}

#canvas {
    position: fixed;
    left: 0;
    bottom: 0;
    width: 100%;
    height: 100px;
}

    </style>
".Replace("\r\n", " ").Replace("backgroundcolor", backgroundcolor).Replace("overlaycolor", overlaycolor).Replace("previousnextbuttonshovercolor", previousnextbuttonshovercolor).Replace("titlehoverbackgroundcolor", titlehoverbackgroundcolor);
            stringinject = @"""" + stringinject + @"""";
            stringinject = @"$(" + stringinject + @" ).appendTo('head');";
            await execScriptHelper(stringinject);
            stringinject = @"

    <!-- Visualizer container -->
	<canvas id=\'canvas\'></canvas>

    <!-- Slideshow container -->
    <div class='slideshow-container container-sm h-100'>
    </div>

    <!-- List container -->
    <div id='list'>
    </div>

    <!-- Overlay container -->
    <div id='overlay'>
    </div>

    <!-- Menushow container -->
    <div class='menushow-container' id='navbar'>
    </div>

    <script>

const bridge = chrome.webview.hostObjects.bridge;

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
                    <div class=\'bg-light random\' style=\'display:float;position:absolute;float:right;right:40px;color:gray;\' onclick=\'random();\' title=\'set random playing\'>
                    <i class=\'fa fa-random\'></i></div>
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
			                    <audio controls preload=\'auto\' autobuffer class=\'mediamp34\' id=\'` + folder + `-` + file + `\' data-name=\'` + file + `\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + file + `\' onended=\'mediaEnded()\'></audio>
                             </div>
                           <div class=\'centered\' style=\'top:97%;align-items:center;position:absolute;\'>` + folder + ` : ` + file + `</div>
                           </div>`;
                visualizeron = true;
            }
            if (file.includes('.mp4') | file.includes('.3gp') | file.includes('.flv') | file.includes('.m4a') | file.includes('.ogg') | file.includes('.webm')) {
    	        htmlString += `<div class=\'mySlides\' align=\'center\'>
                            <div class=\'item\'>
			                    <video controls preload=\'auto\' autobuffer class=\'mediamp34\' id=\'` + folder + `-` + file + `\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + file + `\' onended=\'mediaEnded()\'></video>
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
			                    <img class=\'mediamp34\' id=\'` + folder + `-` + file + `\' src=\'file:///C:/Users/mic/Documents/GitHub/playmedia/med/` + folder + `/` + file + `\' style=\'width:80%;top:50%;left:50%;margin:auto;transform:translate(-50%,-50%);display:block;position:absolute;\'></img>
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
    }
    else {
        collapse = false;
        document.getElementById('list').style.display = 'none';
    }
}

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
    canvas.width = window.innerWidth;
    canvas.height = 300;
    ctx = canvas.getContext('2d');
    WIDTH = canvas.width;
    HEIGHT = canvas.height;
}

function resizingWindow() {
    sizescreeny = window.innerHeight;
    canvas.width = window.innerWidth;
    canvas.height = 300;
    WIDTH = canvas.width;
    HEIGHT = canvas.height;
    document.getElementById('navbar').style.top = '-50px';
    document.getElementById('overlay').style.top = '100%';
    collapse = false;
    document.getElementById('list').style.display = 'none';
}

function responseFunc() { }

    </script>

".Replace("\r\n", " ").Replace("file:///C:/Users/mic/Documents/GitHub/playmedia/med/", "https://appassets/med/").Replace(oldobject, newobject).Replace("backgroundcolor", backgroundcolor);
            stringinject = @"""" + stringinject + @"""";
            stringinject = @"$(document).ready(function(){$('body').append(" + stringinject + @");});";
            await execScriptHelper(stringinject);
        }
        public async static Task<String> execScriptHelper(String script)
        {
            var x = await webView21.ExecuteScriptAsync(script).ConfigureAwait(false);
            return x;
        }
        public void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            closed = true;
            capture.Stop();
            webView21.Dispose();
        }
    }
    public static class Extensions
    {
        public static async Task<string> ExecuteScriptFunctionAsync(this WebView2 webView2, string functionName, params object[] parameters)
        {
            string script = functionName + "(";
            for (int i = 0; i < parameters.Length; i++)
            {
                script += JsonConvert.SerializeObject(parameters[i]);
                if (i < parameters.Length - 1)
                {
                    script += ", ";
                }
            }
            script += ");";
            return await webView2.ExecuteScriptAsync(script);
        }
    }
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class Bridge
    {
        public static Form1 form1 = new Form1();
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