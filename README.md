# adsb2mqtt
* Receive ADS-B messages from dump1090 TCP BaseStation output (default port 30003).
* Build up full flight details from multiple messages.
* Track and publish to MQTT within threshold in nautical miles.

## Usage
**adsb2mqtt** has multiple settings you can set either direclty in appsettings.json or override with environment variables. I run it on a Docker container and use environment variables. See below for applications.
Setting | Default | Description
--- | --- | ---
BEAST_HOST | localhost | dump1090 host
BEAST_PORT | 30003 | dump1090 TCP BaseStation output port
MQTT_SERVER | localhost | MQTT host
MQTT_USE_TLS | false | Connect to MQTT using TLS (mqtts)
MQTT_PORT | 1883 | MQTT port.
MQTT_USERNAME | <username> | MQTT user name
MQTT_PASSWORD | <password> | MQTT password
TOPIC_BASE | ADSB/flight | MQTT topic containing JSON payload
LATITUDE | 47.9073 | Set to your latitude
LONGITUDE | -122.2821 | Set to your longitude
RADIUS_NM | 3.1 | Nautical mile radial distance threshold to publish flight
AIRCRAFT_DB_PATH | /usr/share/dump1090-fa/html/db | [dump1090-fa flight database](https://github.com/flightaware/dump1090/tree/master/public_html/db)

I've tested both local MQTT and remote MQTTS Mosquitto hosts. The included Dockerfile is for an AMD64 server but I've run this standalone on an RPi3. [Link](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) to the .NET install script instructions, including for a Raspbery Pi running Linux.


My main application of **adsb2mqtt** is a seperate MQTT Python client that uses text-to-speech (TTS) to periodically translate the ADSB/flight payload to a wave file using [Microsoft Cognitive Services](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/text-to-speech). Many other TTS options exist. Then publish another MQTT topic with the payload containing the wave filename.

Another option is to generate the wave to a media directory accessible to [HA](https://github.com/home-assistant) and write an Automation to play the wave. I haven't played with HA's recent improvements for TTS though -- might be able to skip creating the wave file.
One other option is a simple mosquitto_sub client on NetBSD to play the wave file. Something roughly like this:
```
#!/bin/sh

subscribe_speech () {
    mosquitto_sub -v -t "ADSB/speech/wave" -u username -P password -h localhost -p 1883 | while read msg
    do
        wave=$(echo $msg | awk '{print $2}')
        curl --silent --output - -X POST -H "Content-Type: text/plain" --data "${wave}" "http://host_serving_tts_wave" | audioplay -d /dev/audio1
    done
}

while true
do
    subscribe_speech
    sleep 5
done
```
Ultimately, It's a simple pleasure to sit on your patio and hear the departure, airline, altitude (or whatever else you want), of planes you can spot flying over.

I keep RADIUS_NM pretty short, basically I want to see the aircraft enough to roughly make out its type. You can also use an ICAO database to get the plane type. Where I live there's a route that planes fly within a 3nm radius usually under 5000ft MSL before landing at the nearby airport.

You don't need to set LATITUDE and LONGITUDE to your precise location (unlike with say dump1090-fa). Since I usually view planes on my porch looking North, I increase the LATITUDE a bit since my house blocks my view of planes approaching from the South.

## NetBSD 10_RC on a Pine64 Rock64 running [dump1090-fa](https://github.com/flightaware/dump1090)
I replaced an aging RPi3 running [piaware](https://github.com/flightaware/piaware) with a 4gb Rock64 running dump1090-fa. Here's a repo for getting this to run: [rtl-sdr-bsd](https://github.com/idatum/rtl-sdr-bsd). Here are more details of the overall project: [Experimenting with RTL-SDR on NetBSD 10](https://www.idatum.net/experimenting-with-rtl-sdr-on-netbsd-10.html).

## FreeBSD 14 on the Rock64 running [dump1090-fa](https://github.com/flightaware/dump1090)
I've since switched from NetBSD 10 to running FreeBSD 14 on the Rock64 device and this resulted in a more stable host for dump1090. It's mainly about better support for RTL-SDR generally with USB on FreeBSD. FreeBSD 14 also now supports .NET 8. 

Regardless of your O/S choice, adsb2mqtt should be able to connect to dump1090 and generate MQTT messages.
