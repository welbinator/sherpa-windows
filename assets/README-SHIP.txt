README - Sherpa for Windows
===========================

1) EXTRACT ALL
   Do not double-click Sherpa.exe from inside the zip window.
   Right-click the zip → Extract All… → pick a folder (Desktop is fine).

2) KEEP THESE FILES TOGETHER
   Sherpa.exe
   WebView2Loader.dll
   Microsoft.Web.WebView2.Core.dll

   (Ignore / delete Sherpa.pdb if you ever see it — that is a debug file, not needed.)

3) UNBLOCK (if Windows downloaded the zip)
   Right-click the extracted folder → Properties
   If you see an Unblock checkbox at the bottom, tick it → Apply → OK
   Or right-click Sherpa.exe → Properties → Unblock.

4) RUN
   Double-click Sherpa.exe

5) SMARTSCREEN
   If Windows says "Windows protected your PC":
   More info → Run anyway

If it still will not open, look for:
   - sherpa-crash.log next to Sherpa.exe
   - %LocalAppData%\Sherpa\sherpa-crash.log
   - sherpa-crash.log on your Desktop
and send that file for help.
