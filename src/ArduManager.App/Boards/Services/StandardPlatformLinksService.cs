using ArduboardsManager.App.Models;

namespace ArduboardsManager.App.Services;

public sealed class StandardPlatformLinksService
{
    private static readonly IReadOnlyList<StandardPlatformLink> BuiltInLinks = new List<StandardPlatformLink>
    {
        new()
        {
            Name = "ESP8266",
            Url = "https://arduino.esp8266.com/stable/package_esp8266com_index.json"
        },
        new()
        {
            Name = "ESP32",
            Url = "https://espressif.github.io/arduino-esp32/package_esp32_index.json"
        },
        new()
        {
            Name = "RP2040/RP2350",
            Url = "https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json"
        },
        new()
        {
            Name = "STM32duino",
            Url = "https://github.com/stm32duino/BoardManagerFiles/raw/main/package_stmicroelectronics_index.json"
        },
        new()
        {
            Name = "Arduino official",
            Url = "https://downloads.arduino.cc/packages/package_index.json"
        },
        new()
        {
            Name = "MicroCore",
            Url = "https://mcudude.github.io/MicroCore/package_MCUdude_MicroCore_index.json"
        },
        new()
        {
            Name = "ATTinyCore",
            Url = "http://drazzy.com/package_drazzy.com_index.json"
        },
        new()
        {
            Name = "MiniCore",
            Url = "https://mcudude.github.io/MiniCore/package_MCUdude_MiniCore_index.json"
        },
        new()
        {
            Name = "MightyCore",
            Url = "https://mcudude.github.io/MightyCore/package_MCUdude_MightyCore_index.json"
        },
        new()
        {
            Name = "MegaCore",
            Url = "https://mcudude.github.io/MegaCore/package_MCUdude_MegaCore_index.json"
        },
        new()
        {
            Name = "LGT8fx",
            Url = "https://raw.githubusercontent.com/dbuezas/lgt8fx/master/package_lgt8fx_index.json"
        }
    };

    public IReadOnlyList<StandardPlatformLink> Load() => BuiltInLinks;
}
