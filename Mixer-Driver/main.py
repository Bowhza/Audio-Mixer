from pycaw.pycaw import AudioUtilities, ISimpleAudioVolume
import serial.tools.list_ports
import json

# Constants
BAUD_RATE = 115200

# Global variables
audio_channels = {}

applications = ["Discord.exe", "Spotify.exe", "steam.exe", "", ""]

if __name__ == '__main__':
    sessions = AudioUtilities.GetAllSessions()
    target_sessions = [session for session in sessions if session.Process and session.Process.name() in applications]

    # Get a list of all available COM ports
    available_ports = list(serial.tools.list_ports.comports())

    # Print information about each port
    print("Available COM ports:")
    for i, port in enumerate(available_ports):
        print(f"{i + 1}. Port: {port.device}, Description: {port.description}, Hardware ID: {port.hwid}")

    # Prompt the user to select a port
    while True:
        try:
            selected_index = int(input("Enter the index of the port you want to connect to: ")) - 1
            if 0 <= selected_index < len(available_ports):
                selected_port = available_ports[selected_index].device
                print(f"Selected port: {selected_port}")

                # Open a serial connection to the selected port
                ser = serial.Serial(selected_port, BAUD_RATE)  # Adjust the baud rate as needed

                # Check if the port is open
                if ser.is_open:
                    print(f"Serial connection to {selected_port} established successfully.")
                    break
                else:
                    print(f"Failed to establish serial connection to {selected_port}.")
        except ValueError:
            print("Invalid input. Please enter a valid index.")

    # Main loop
    while True:
        try:
            # Read a line of data from the serial port
            line = ser.readline().decode('utf-8').strip()
            # Convert to JSON
            json_data = json.loads(line)
            # Store the volume percentage for each channel
            audio_channels[json_data["Channel"]] = json_data["Percentage"]

            for key, value in audio_channels.items():
                print(f"Channel: {key}, Volume: {value}")

            for i, session in enumerate(target_sessions):
                # Check if the channel index exists in audio_channels
                if i in audio_channels:
                    # Ensure volume percentage is within range [0, 100]
                    volume_percentage = max(0, min(100, audio_channels[i]))
                    volume = session._ctl.QueryInterface(ISimpleAudioVolume)
                    volume.SetMasterVolume(volume_percentage / 100.0, None)
                else:
                    print(f"Channel {i} not found in audio_channels.")

        except Exception as e:
            print(f"Error: {e}")
            # Handle the error gracefully (close serial port, etc.)
            ser.close()
