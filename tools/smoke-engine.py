"""Import retained pyOCD backends without enumerating or opening hardware."""
import pyocd
from pyocd.probe import cmsis_dap_probe, stlink_probe, jlink_probe
from pyocd.probe.tcp_client_probe import TCPClientProbe
from pyocd.probe.pydapaccess.interface import HidApiUSB, PyUSBv2
from pyocd.target.pack import pack_target
import libusb_package

if not HidApiUSB.isAvailable or not PyUSBv2.isAvailable:
    raise RuntimeError('Required CMSIS-DAP USB backend is unavailable')
if libusb_package.get_libusb1_backend() is None:
    raise RuntimeError('Packaged libusb could not be loaded')
print('Pruned pyOCD USB/TCP/pack backends imported:', pyocd.__version__)
