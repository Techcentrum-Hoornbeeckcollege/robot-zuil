# Wiring: Raspberry Pi ↔ ESP32

Both parts run at 3.3V logic, so **no level shifter**. They do need a solid
common ground — a missing ground is the most common cause of an i2c bus that
"works sometimes".

| Signal | Pi (BCM / header pin) | ESP32 | Notes |
|---|---|---|---|
| SDA | GPIO 2 / pin 3 | GPIO 21 | Pi has 1.8kΩ pull-ups on board; do not add more |
| SCL | GPIO 3 / pin 5 | GPIO 22 | |
| Attention | GPIO 17 / pin 11 | GPIO 4 | ESP32 pulls LOW to request a read |
| GND | pin 6 | GND | **Required.** Share it. |

Do **not** connect the Pi's 5V or 3.3V rail to the ESP32 if the ESP32 has its
own supply; power it from one source only.

Keep SDA/SCL short — under ~20cm. i2c was designed for traces on one board, not
for cable runs inside a column, and the symptom of too much capacitance is
intermittent corruption rather than a clean failure.

## Enable the bus

```bash
# Raspberry Pi OS
sudo raspi-config   # Interface Options → I2C → Enable

# Ubuntu on a Pi
echo 'dtparam=i2c_arm=on' | sudo tee -a /boot/firmware/config.txt
sudo reboot
```

Then confirm the ESP32 is answering at `0x42`:

```bash
sudo apt install -y i2c-tools
i2cdetect -y 1
```

If `0x42` does not appear: check the ground, check the ESP32 is actually running
(`pio device monitor` should print `zuil firmware ready at 0x42`), and check SDA
and SCL are not swapped.

## Bus speed

100 kHz, set in `firmware/esp32/src/main.cpp`. Do not raise it. ESP32 i2c-slave
mode cannot stretch the clock reliably, and a faster bus turns that from a rare
retry into constant corruption. See
[device-protocol.md](../schema/device-protocol.md).

## Permissions

The service runs as the `zuil` user, not root. `deploy/install.sh` adds that
user to the `i2c` and `gpio` groups, which is what grants access to
`/dev/i2c-1`. A network-facing service does not need to be root to toggle a pin.
