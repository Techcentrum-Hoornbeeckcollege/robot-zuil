// ESP32 i2c slave for the wayfinding column.
//
// Role: the Raspberry Pi is bus master and tells us which indicator to light;
// we drive the physical output and, when we have something to report, pull the
// attention line low so the Pi comes and asks.
//
// Two known constraints:
//  * ESP32 i2c-slave support is the weak part of that peripheral — it cannot
//    stretch the clock reliably. So onReceive() must return fast and must not
//    do real work; it only stages a reply for the next onRequest().
//  * A slave cannot start a transfer at all, hence the attention GPIO.

#include <Arduino.h>
#include <Wire.h>

#include "protocol.h"

// Pulled LOW to tell the Pi we have an event waiting. Open-drain style: the Pi
// holds it up with an internal pull-up.
static const uint8_t PIN_ATTENTION = 4;

// TODO: replace with the real output driver once the physical build is decided
// (addressable LED strip, relay bank for lit signs, stepper for a rotating
// arrow, ...). Until then this is a placeholder so the protocol can be tested
// end-to-end with the Pi.
static const uint8_t PIN_STATUS_LED = 2;

static volatile uint8_t g_rx[ZUIL_MAX_FRAME];
static volatile uint8_t g_rx_len = 0;

static uint8_t g_reply[ZUIL_MAX_FRAME];
static uint8_t g_reply_len = 0;

static volatile uint8_t g_active_signal = 0;
static volatile bool g_command_pending = false;

static void stageReply(uint8_t code, const uint8_t *payload, uint8_t payload_len) {
  g_reply_len = zuil_encode(code, payload, payload_len, g_reply);
}

// i2c ISR context: keep this short. Copy the bytes out and get off the bus.
static void onReceive(int count) {
  uint8_t len = 0;

  while (Wire.available() && len < ZUIL_MAX_FRAME) {
    g_rx[len++] = (uint8_t)Wire.read();
  }

  // Drain anything that did not fit, or the next transfer starts mid-frame.
  while (Wire.available()) {
    Wire.read();
  }

  g_rx_len = len;
  g_command_pending = true;
  (void)count;
}

// The master is reading. Hand back whatever the last command staged.
static void onRequest() {
  if (g_reply_len == 0) {
    stageReply(ZUIL_RSP_NACK, nullptr, 0);
  }

  Wire.write(g_reply, g_reply_len);
}

static void applyDirection(uint8_t signalId) {
  g_active_signal = signalId;

  // TODO: drive the real indicator for `signalId`. Mapping lives in
  // config/rooms.json on the host, so firmware only needs the id.
  digitalWrite(PIN_STATUS_LED, signalId == 0 ? LOW : HIGH);
}

static void handleCommand() {
  uint8_t len = g_rx_len;
  uint8_t frame[ZUIL_MAX_FRAME];

  for (uint8_t i = 0; i < len; i++) {
    frame[i] = g_rx[i];
  }

  g_command_pending = false;

  if (!zuil_verify(frame, len)) {
    // Bad CRC: NACK and let the host retry rather than acting on a byte that
    // may have been corrupted in transit.
    stageReply(ZUIL_RSP_NACK, nullptr, 0);
    return;
  }

  const uint8_t cmd = frame[1];

  switch (cmd) {
    case ZUIL_CMD_PING:
      stageReply(ZUIL_RSP_PONG, nullptr, 0);
      break;

    case ZUIL_CMD_SHOW_DIRECTION: {
      const uint8_t signalId = frame[2];
      applyDirection(signalId);
      stageReply(ZUIL_RSP_ACK, &signalId, 1);
      break;
    }

    case ZUIL_CMD_CLEAR:
      applyDirection(0);
      stageReply(ZUIL_RSP_ACK, nullptr, 0);
      break;

    case ZUIL_CMD_READ_EVENT:
      // Nothing to report yet; de-assert the attention line.
      digitalWrite(PIN_ATTENTION, HIGH);
      stageReply(ZUIL_RSP_NO_EVENT, nullptr, 0);
      break;

    default:
      stageReply(ZUIL_RSP_NACK, nullptr, 0);
      break;
  }
}

void setup() {
  Serial.begin(115200);

  pinMode(PIN_STATUS_LED, OUTPUT);
  digitalWrite(PIN_STATUS_LED, LOW);

  pinMode(PIN_ATTENTION, OUTPUT);
  digitalWrite(PIN_ATTENTION, HIGH);  // idle high; LOW means "come read from me"

  Wire.onReceive(onReceive);
  Wire.onRequest(onRequest);

  // 100 kHz, not 400: short wires and a slow clock are what make ESP32 slave
  // mode behave. Both the Pi and the ESP32 are 3.3V, so no level shifter is
  // needed, but they do need a solid common ground.
  Wire.begin((uint8_t)ZUIL_I2C_ADDRESS, SDA, SCL, 100000);

  Serial.printf("zuil firmware ready at 0x%02X\n", ZUIL_I2C_ADDRESS);
}

void loop() {
  if (g_command_pending) {
    handleCommand();
  }

  delay(1);
}
