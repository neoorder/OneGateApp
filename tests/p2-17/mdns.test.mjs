import test from 'node:test';
import assert from 'node:assert/strict';
import dgram from 'node:dgram';
import { EventEmitter } from 'node:events';
import { RemoteDebuggerAdvertiser } from '../../OneGate.Codex/onegate/skills/onegate-dapp-debug/scripts/runtime/remote-debugger-advertiser.mjs';

test('announcements do not answer themselves; only service queries trigger an answer', async () => {
  const original = dgram.createSocket;
  const socket = new EventEmitter();
  const sent = [];
  socket.bind = (_, __, callback) => callback();
  socket.addMembership = socket.setMulticastTTL = () => {};
  socket.send = (packet, _, __, callback) => { sent.push(packet); callback(); };
  socket.close = callback => callback();
  dgram.createSocket = () => socket;
  const advertiser = new RemoteDebuggerAdvertiser({ debuggerId: 'test', debuggerName: 'Test', port: 12345, addresses: ['192.168.1.2'] });
  try {
    await advertiser.start();
    socket.emit('message', sent[0]);
    assert.equal(sent.length, 1, 'a response packet must not trigger another response');
    socket.emit('message', Buffer.from('_onegate-debug garbage'));
    assert.equal(sent.length, 1);
    const query = Buffer.concat([Buffer.from([0,0,0,0,0,1,0,0,0,0,0,0]), ...['_onegate-debug','_tcp','local'].map(label => Buffer.concat([Buffer.from([label.length]), Buffer.from(label)])), Buffer.from([0,0,12,0,1])]);
    socket.emit('message', query);
    assert.equal(sent.length, 2);
    socket.emit('message', query.subarray(0, query.length - 1));
    assert.equal(sent.length, 2, 'truncated query must not be answered');
  } finally {
    await advertiser.stop();
    dgram.createSocket = original;
  }
});
