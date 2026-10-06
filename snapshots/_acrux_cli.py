"""Locate the Acrux headless binary and make it runnable.

On Linux the apphost needs DOTNET_ROOT to point at the shared runtime, and the
build output folder differs from Windows (`net10.0` vs `net10.0-windows`), so
every snapshot tool must resolve both before spawning the CLI.
"""
import os
import shutil


def binary_path():
    if os.name == "nt":
        return os.path.join("Acrux", "bin", "Debug", "net10.0-windows", "Acrux.exe")
    return os.path.join("Acrux", "bin", "Debug", "net10.0", "Acrux")


def environment():
    env = dict(os.environ)
    if os.name != "nt" and not env.get("DOTNET_ROOT"):
        dotnet = shutil.which("dotnet")
        if dotnet:
            env["DOTNET_ROOT"] = os.path.dirname(os.path.realpath(dotnet))
    return env


def command(args):
    return [binary_path()] + [str(a) for a in args]
