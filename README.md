# DS4 Battery Lightbar Mapper

A Windows application that reads connected DualShock 4 (DS4) controller(s) and maps the lightbar color to the battery percentage.

## Features

- **Real-time monitoring** of connected DS4 controllers
- **Battery percentage display** with visual progress bar
- **Configurable lightbar gradient** between custom low-battery (0%) and high-battery (100%) colors
- **Live preview** of lightbar color
- **Auto-detection** of connected/disconnected controllers
- **Dark theme** UI optimized for gaming setups

## Requirements

- Windows 10 or later
- .NET 6.0 or later
- DualShock 4 controller(s) connected via USB or Bluetooth

## Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/quincy2112/DS4-Battery-Lightbar.git
   cd DS4-Battery-Lightbar
   ```

2. Install dependencies:
   ```bash
   dotnet restore
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

4. Run the application:
   ```bash
   dotnet run
   ```

## Usage

1. Launch the application
2. Connect your DS4 controller(s) via USB or Bluetooth
3. The app will automatically detect connected controllers
4. Click the low/high battery color swatches to choose custom endpoint colors
5. Watch the lightbar color and battery percentage update in real-time

## Color Mapping Logic

The application linearly interpolates between the selected low-battery color (0%) and high-battery color (100%) to generate the current lightbar color.

## Project Structure

- **Program.cs** - Application entry point
- **MainForm.cs** - Main UI window and controller status display
- **DS4ControllerManager.cs** - Manages multiple connected controllers
- **DS4Controller.cs** - Individual controller interface and HID communication
- **DS4BatteryMapper.csproj** - Project configuration

## Dependencies

- [HidLibrary](https://github.com/jcoenraadts/HidLibrary) - USB HID device communication

## License

MIT License - feel free to use and modify as needed.

## Troubleshooting

### No controllers detected
- Ensure your DS4 is properly connected (USB or paired via Bluetooth)
- Try disconnecting and reconnecting the controller
- Check Device Manager to confirm the controller is recognized

### Application crashes on startup
- Ensure .NET 6.0 or later is installed
- Try running with administrator privileges

## Future Enhancements

- [ ] Custom color presets
- [ ] Battery statistics and history
- [ ] Support for additional controllers (Xbox, etc.)
- [ ] Tray icon with quick status
