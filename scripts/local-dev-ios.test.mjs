import assert from 'node:assert/strict';
import { test } from 'node:test';
import { deviceState, profiles } from './local-dev-ios.mjs';

const [phone] = profiles;
const device = (overrides = {}) => ({
  udid: 'A',
  name: phone.name,
  state: 'Shutdown',
  deviceTypeIdentifier: phone.deviceType,
  ...overrides
});

test('a simulator matches its profile by name, device type and runtime', () => {
  const list = (runtime, ...devices) => ({ devices: { [runtime]: devices } });
  assert.equal(deviceState(list(phone.runtime), phone).state, 'missing');
  const found = deviceState(list(phone.runtime, device()), phone);
  assert.equal(found.state, 'configured');
  assert.equal(found.device.udid, 'A');
  assert.equal(
    deviceState(
      list('com.apple.CoreSimulator.SimRuntime.iOS-27-2', device()),
      phone
    ).state,
    'config mismatch'
  );
  assert.equal(
    deviceState(
      list(
        phone.runtime,
        device({ deviceTypeIdentifier: profiles[2].deviceType })
      ),
      phone
    ).state,
    'config mismatch'
  );
  assert.equal(
    deviceState(list(phone.runtime, device(), device({ udid: 'B' })), phone)
      .state,
    'duplicate name'
  );
});
