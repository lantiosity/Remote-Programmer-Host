"""Regression tests for host-owned remote chunking; no device or firmware needed."""
import json
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'WireLink.App/engine'))
import wirelink_pyocd
wirelink_pyocd.install()
from pyocd.probe.tcp_client_probe import RemoteMemoryInterface

class Probe:
    def __init__(self):
        self.calls = []
        self.memory = {}
        self.fail_at = None
    def _perform_request(self, name, handle, addr, value):
        if self.fail_at == len(self.calls):
            raise IOError('disconnected')
        self.calls.append((name, handle, addr, value))
        wire = json.dumps({'id': 2147483647, 'request': name, 'arguments': [handle, addr, value]})
        assert len(wire.encode()) < 20480
        stride = 4 if name.endswith('32') else 1
        if name.startswith('write'):
            for i, item in enumerate(value): self.memory[addr + i * stride] = item
        else:
            return [self.memory.get(addr + i * stride, 0) for i in range(value)]

class RemoteTests(unittest.TestCase):
    def test_large_ff_blocks_preserve_values_addresses_and_order(self):
        for suffix, count, value in [('8', 65536, 255), ('32', 8192, 0xffffffff)]:
            with self.subTest(suffix=suffix):
                probe = Probe(); memory = RemoteMemoryInterface(probe, 3)
                data = [value] * count
                getattr(memory, 'write_memory_block' + suffix)(0x20000000, data)
                self.assertEqual(getattr(memory, 'read_memory_block' + suffix)(0x20000000, count), data)
                self.assertGreater(len(probe.calls), 2)
    def test_failure_stops_without_resending_or_skipping(self):
        probe = Probe(); probe.fail_at = 1
        with self.assertRaisesRegex(IOError, 'disconnected'):
            RemoteMemoryInterface(probe, 0).write_memory_block8(0, [255] * 10000)
        self.assertEqual(len(probe.calls), 1)
    def test_empty_blocks_do_not_send_invalid_zero_count_requests(self):
        probe = Probe(); memory = RemoteMemoryInterface(probe, 0)
        memory.write_memory_block32(0, [])
        self.assertEqual(memory.read_memory_block8(0, 0), [])
        self.assertEqual(probe.calls, [])

if __name__ == '__main__': unittest.main()
