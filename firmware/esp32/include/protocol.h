// Wire format shared with the host.
//
// MUST stay byte-for-byte in step with src/Zuil.Device/Framing.cs.
// The contract is documented in schema/device-protocol.md.
//
// Frame layout:  [len][cmd][payload 0..n][crc8]
//   len  = number of bytes after len   (cmd + payload + crc)
//   crc8 = CRC-8/ATM over [len][cmd][payload], polynomial 0x07, init 0x00
//
// i2c provides no message framing of its own, so `len` is what tells the slave
// when a command is complete. It provides no integrity checking either, which is
// why every frame carries a CRC: a glitch on a ribbon cable arrives as a
// perfectly valid-looking byte, and a corrupted signal id points a visitor at
// the wrong room.

#pragma once

#include <stdint.h>

#define ZUIL_I2C_ADDRESS 0x42
#define ZUIL_MAX_PAYLOAD 16
#define ZUIL_MAX_FRAME (2 + ZUIL_MAX_PAYLOAD + 1)

// Host -> device
#define ZUIL_CMD_PING 0x01
#define ZUIL_CMD_SHOW_DIRECTION 0x02  // payload: [signalId]
#define ZUIL_CMD_CLEAR 0x03
#define ZUIL_CMD_READ_EVENT 0x04

// Device -> host
#define ZUIL_RSP_PONG 0x81
#define ZUIL_RSP_ACK 0x82
#define ZUIL_RSP_NACK 0x83
#define ZUIL_RSP_NO_EVENT 0x84
#define ZUIL_RSP_FAULT 0x85

static inline uint8_t zuil_crc8(const uint8_t *data, uint8_t length) {
  uint8_t crc = 0x00;

  for (uint8_t i = 0; i < length; i++) {
    crc ^= data[i];

    for (uint8_t bit = 0; bit < 8; bit++) {
      crc = (crc & 0x80) ? (uint8_t)((crc << 1) ^ 0x07) : (uint8_t)(crc << 1);
    }
  }

  return crc;
}

// Builds a frame into `out` and returns its total length.
static inline uint8_t zuil_encode(uint8_t code, const uint8_t *payload,
                                  uint8_t payload_len, uint8_t *out) {
  out[0] = (uint8_t)(payload_len + 2);
  out[1] = code;

  for (uint8_t i = 0; i < payload_len; i++) {
    out[2 + i] = payload[i];
  }

  uint8_t total = (uint8_t)(payload_len + 3);
  out[total - 1] = zuil_crc8(out, (uint8_t)(total - 1));
  return total;
}

// Validates a received frame. Returns 1 on success.
static inline uint8_t zuil_verify(const uint8_t *frame, uint8_t length) {
  if (length < 3) {
    return 0;
  }

  uint8_t declared = frame[0];
  if (declared < 2 || declared > ZUIL_MAX_PAYLOAD + 2 || declared + 1 > length) {
    return 0;
  }

  return zuil_crc8(frame, declared) == frame[declared] ? 1 : 0;
}
