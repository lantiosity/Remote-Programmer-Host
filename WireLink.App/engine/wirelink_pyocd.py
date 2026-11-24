"""Host-owned pyOCD entry point with bounded remote memory transactions."""
import runpy
import sys


def install():
    from pyocd.probe.tcp_client_probe import RemoteMemoryInterface, TCPClientProbe
    if getattr(RemoteMemoryInterface, '_wirelink_bounded', False):
        return
    # Decimal JSON with default spaces remains below 20480 bytes even for FFs.
    for suffix, limit, stride in [('8', 3072, 1), ('32', 1024, 4)]:
        name = 'write_memory_block' + suffix
        original = getattr(RemoteMemoryInterface, name)
        def write(self, addr, data, _method=original, _limit=limit, _stride=stride, **attrs):
            for offset in range(0, len(data), _limit):
                _method(self, addr + offset * _stride, data[offset:offset + _limit], **attrs)
        setattr(RemoteMemoryInterface, name, write)
        name = 'read_memory_block' + suffix
        original = getattr(RemoteMemoryInterface, name)
        def read(self, addr, size, _method=original, _limit=limit, _stride=stride, **attrs):
            result = []
            for offset in range(0, size, _limit):
                count = min(_limit, size - offset)
                part = _method(self, addr + offset * _stride, count, **attrs)
                if len(part) != count:
                    raise IOError('Remote memory reply length mismatch')
                result.extend(part)
            return result
        setattr(RemoteMemoryInterface, name, read)
    original_write = TCPClientProbe.write_ap_multiple
    def write_ap(self, addr, values):
        for offset in range(0, len(values), 1024):
            original_write(self, addr, values[offset:offset + 1024])
    original_read = TCPClientProbe.read_ap_multiple
    def read_ap(self, addr, count, now=True):
        callbacks = [original_read(self, addr, min(1024, count - offset), now=False)
                     for offset in range(0, count, 1024)]
        def complete():
            result = []
            for callback in callbacks:
                result.extend(callback())
            if len(result) != count:
                raise IOError('Remote AP reply length mismatch')
            return result
        return complete() if now else complete
    TCPClientProbe.write_ap_multiple = write_ap
    TCPClientProbe.read_ap_multiple = read_ap
    RemoteMemoryInterface._wirelink_bounded = True


if __name__ == '__main__':
    install()
    sys.argv[0] = 'pyocd'
    runpy.run_module('pyocd', run_name='__main__')
