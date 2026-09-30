// Protocol vocabulary shared with the page-host processes lives in UpBrowser.PageContract;
// these aliases keep the existing in-repo call sites reading the same after the move.
global using TabMsg = UpBrowser.PageContract.TabMsg;
global using FrameMode = UpBrowser.PageContract.FrameMode;
global using PageResponsiveness = UpBrowser.PageContract.PageResponsiveness;
global using RestartPolicy = UpBrowser.PageContract.RestartPolicy;
global using CrashKind = UpBrowser.PageContract.CrashKind;
global using FrameChannel = UpBrowser.PageContract.FrameChannel;
global using FrameMeta = UpBrowser.PageContract.FrameMeta;
global using DamageRect = UpBrowser.PageContract.DamageRect;
global using PageProtocol = UpBrowser.PageContract.PageProtocol;
global using DevToolsWire = UpBrowser.PageContract.DevToolsWire;
