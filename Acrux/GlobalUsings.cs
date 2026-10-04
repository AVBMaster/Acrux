// Protocol vocabulary shared with the page-host processes lives in Acrux.PageContract;
// these aliases keep the existing in-repo call sites reading the same after the move.
global using TabMsg = Acrux.PageContract.TabMsg;
global using FrameMode = Acrux.PageContract.FrameMode;
global using PageResponsiveness = Acrux.PageContract.PageResponsiveness;
global using RestartPolicy = Acrux.PageContract.RestartPolicy;
global using CrashKind = Acrux.PageContract.CrashKind;
global using FrameChannel = Acrux.PageContract.FrameChannel;
global using FrameMeta = Acrux.PageContract.FrameMeta;
global using DamageRect = Acrux.PageContract.DamageRect;
global using PageProtocol = Acrux.PageContract.PageProtocol;
global using DevToolsWire = Acrux.PageContract.DevToolsWire;
