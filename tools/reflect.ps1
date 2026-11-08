Add-Type -Path 'C:\Users\Monty\.nuget\packages\microsoft.web.webview2\1.0.2903.40\lib\net462\Microsoft.Web.WebView2.Core.dll'
Add-Type -Path 'C:\Users\Monty\.nuget\packages\microsoft.web.webview2\1.0.2903.40\lib\net462\Microsoft.Web.WebView2.Wpf.dll'

"CoreWebView2.Controller: " + ([Microsoft.Web.WebView2.Core.CoreWebView2].GetProperty('Controller') -ne $null)
"Wpf.WebView2Controller: " + ([Microsoft.Web.WebView2.Wpf.WebView2].GetProperty('CoreWebView2Controller') -ne $null)
"Wpf.Props: " + (([Microsoft.Web.WebView2.Wpf.WebView2].GetProperties() | Select-Object -ExpandProperty Name) -join ',')
"AccelProps: " + (([Microsoft.Web.WebView2.Core.CoreWebView2AcceleratorKeyPressedEventArgs].GetProperties() | Select-Object -ExpandProperty Name) -join ',')
"DownloadStartingProps: " + (([Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs].GetProperties() | Select-Object -ExpandProperty Name) -join ',')
"DownloadOpProps: " + (([Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation].GetProperties() | Select-Object -ExpandProperty Name) -join ',')
