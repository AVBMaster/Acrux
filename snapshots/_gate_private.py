"""Run the marker gate against a private copy of the app bin.

The shared build folder belongs to whichever build ran last, and more than one session can be working
in this tree at once; a gate must therefore render with a binary nobody else is overwriting. Copy the
build output (`cp -a Acrux/bin/Debug/net10.0 /tmp/apprun`) and drop the core dll under test into it
(`cp /tmp/coredll/Acrux.Core.dll /tmp/apprun/`), then run this from the repository root with the same
arguments `marker_gate.py` takes — `--all`, or a list of batch numbers. Only one gate may run at a
time: every batch is rendered into the same snapshots/out/_gate.png.
"""
import os, runpy, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _acrux_cli

PRIVATE = "/tmp/apprun/Acrux"
if os.path.exists(PRIVATE):
    _acrux_cli.binary_path = lambda: PRIVATE
sys.argv = ["marker_gate.py"] + sys.argv[1:]
runpy.run_path(os.path.join(HERE, "marker_gate.py"), run_name="__main__")
