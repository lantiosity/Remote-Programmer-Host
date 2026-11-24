"""Exercise bundled pyOCD against real firmware JSON parsing and a fake probe.

Run with a Python that has pyOCD, e.g. portable/python/python.exe -B
tools/check-firmware-contract.py --firmware ../firmware. Requires native GCC
and the firmware's resolved cJSON dependency. Writes only host/obj; no hardware.
This diagnostic reports known limits, rather than treating them as compatibility.
"""
import argparse
import ctypes
import json
from pathlib import Path
import runpy
import socketserver
import sys
import threading

host = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--firmware', required=True, type=Path)
firmware = parser.parse_args().firmware.resolve()
out = host / 'obj' / 'firmware-contract'
builder = firmware / 'tests/build_remote.py'
if not builder.is_file():
    raise SystemExit('The supplied firmware checkout must have tests/build_remote.py.')
lib = runpy.run_path(str(builder))['build_remote'](out)
TCP, BLE, TOKEN = 1, 2, 1
records = []

def dispatch(sink, raw):
    output = ctypes.create_string_buffer(20480)
    size = lib.test_link_request(sink, TOKEN, raw, len(raw), output)
    return output.raw[:size]

def ble_request(name, args=()):
    raw = json.dumps({'id': 1, 'request': name, 'arguments': args}).encode()
    reply = json.loads(dispatch(BLE, raw))
    assert reply['status'] == 0, reply
    return reply.get('result')

class Handler(socketserver.StreamRequestHandler):
    closed = threading.Event()
    ble_handle = None

    def handle(self):
        self.connection.settimeout(5)
        lib.test_link_init()
        try:
            while line := self.rfile.readline():
                raw = line.rstrip(b'\r\n')
                response = dispatch(TCP, raw)
                reply = json.loads(response)
                name = json.loads(raw)['request']
                records.append({'request': name, 'request_bytes': len(raw), 'status': reply['status'], 'error': reply.get('error')})
                if name == 'disconnect':
                    ble_request('connect', ['SWD'])
                    Handler.ble_handle = ble_request('get_memory_interface_for_ap', [1, 7])
                self.wfile.write(response + b'\n')
        finally:
            lib.link_session_set(TCP, -1)
            lib.test_link_poll()
        Handler.closed.set()

sys.path.insert(0, str(host / 'WireLink.App/engine'))
import wirelink_pyocd
wirelink_pyocd.install()
from pyocd.probe.tcp_client_probe import TCPClientProbe
from pyocd.probe.debug_probe import DebugProbe
from pyocd.coresight.ap import APv1Address, APv2Address
results = []
def check(name, operation, expected_error=None):
    before = len(records)
    try:
        operation()
        results.append({'name': name, 'result': 'unexpected success' if expected_error else 'compatible', 'wire': records[before:]})
    except Exception as error:
        result = 'confirmed limitation' if expected_error and expected_error in str(error) else 'unexpected failure'
        results.append({'name': name, 'result': result, 'error': str(error), 'wire': records[before:]})

def close_after_handoff(probe):
    probe.disconnect()
    probe.close()
    assert not probe.is_open and Handler.closed.wait(5)
    assert Handler.ble_handle is not None
    ble_request('read_mem', [Handler.ble_handle, 0x20000000, 32])

with socketserver.TCPServer(('127.0.0.1', 0), Handler) as server:
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    probe = TCPClientProbe(f'127.0.0.1:{server.server_address[1]}')
    check('hello/open', probe.open)
    check('read capabilities', lambda: probe.capabilities)
    check('SWD connect', lambda: probe.connect(DebugProbe.Protocol.SWD))
    memory = probe.get_memory_interface_for_ap(APv1Address(7))
    check('APv1 APSEL 7', lambda: memory.read_memory(0x20000000))
    assert ctypes.c_uint.in_dll(lib, 'test_apsel').value == 7
    bits = 0x1A019BC0EA2E3DDAFE986852D956209F392
    check('SWJ 136-bit integer', lambda: probe.swj_sequence(136, bits))
    actual = bytes((ctypes.c_uint8 * 8192).in_dll(lib, 'test_data'))
    assert int.from_bytes(actual[:17], 'little') == bits & ((1 << 136) - 1)
    check('4 KiB patterned block8 write', lambda: memory.write_memory_block8(0x20000000, [i % 256 for i in range(4096)]))
    check('4 KiB FF block8 write', lambda: memory.write_memory_block8(0x20000000, [255] * 4096))
    check('4 KiB block8 read', lambda: memory.read_memory_block8(0x20000000, 4096))
    check('5104-byte block8 read', lambda: memory.read_memory_block8(0x20000000, 5104))
    check('5105-byte block8 read', lambda: memory.read_memory_block8(0x20000000, 5105))
    check('8 KiB all-ones block32 write', lambda: memory.write_memory_block32(0x20000000, [0xffffffff] * 2048))
    check('APv2', lambda: probe.get_memory_interface_for_ap(APv2Address(0)), 'only APv1')
    check('nested lock/unlock', lambda: (probe.lock(), probe.lock(), probe.unlock(), probe.unlock()))
    check('close after BLE handoff', lambda: close_after_handoff(probe))
    server.shutdown()
    thread.join(3)
(out / 'results.json').write_text(json.dumps(results, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
for row in results:
    print(row['name'], ':', row['result'], row.get('error', ''))
assert all(r['result'] in ('compatible', 'confirmed limitation') for r in results)
