from os import listdir, remove
from os.path import isfile, join
import json

def convert_flights(fpath):
    flights = [join(fpath, f) for f in listdir(fpath) if isfile(join(fpath, f))]
    for flight in flights:
        flightv2 = None
        with open(flight) as f:
            try:
                flightv2 = json.loads(f.read())
            except json.JSONDecodeError:
                continue
        if flightv2:
            flights0 = flightv2['FlightInfoExResult']['flights'][0]
            flightv4_element = {}
            flightv4_element['ident'] = flights0['ident']
            flightv4_element['aircraft_type'] = flights0['aircrafttype']
            flightv4_element['origin'] = {'code': flights0['origin'],
                                      'name': flights0['originName'],
                                      'city': flights0['originCity']}
            flightv4_element['destination'] = {'code': flights0['destination'],
                                      'name': flights0['destinationName'],
                                      'city': flights0['destinationCity']}
            flightv4 = {'flights': [flightv4_element]}
            print(json.dumps(flightv4))
            with open(flight, 'w+') as f:
                f.write(json.dumps(flightv4))


def convert_aircraft(fpath):
    aircraft = [join(fpath, f) for f in listdir(fpath) if isfile(join(fpath, f))]
    for aircraft0 in aircraft:
        aircraftv2 = None
        with open(aircraft0) as f:
            try:
                aircraftv2 = json.loads(f.read())
            except json.JSONDecodeError:
                continue
        if aircraftv2:
            aircraftv4 = aircraftv2['AircraftTypeResult']
            print(json.dumps(aircraftv4))
            with open(aircraft0, 'w+') as f:
                f.write(json.dumps(aircraftv4))

def convert_airlines(fpath):
    airlines = [join(fpath, f) for f in listdir(fpath) if isfile(join(fpath, f))]
    for airline in airlines:
        airlinev2 = None
        with open(airline) as f:
            try:
                airlinev2 = json.loads(f.read())
            except json.JSONDecodeError:
                continue
        if airlinev2 and 'AirlineInfoResult' in airlinev2:
            airlinev4 = airlinev2['AirlineInfoResult']
            print(json.dumps(airlinev4))
            with open(airline, 'w+') as f:
                f.write(json.dumps(airlinev4))
        else:
            remove(airline)

convert_flights('/lab/adsb/flights')
convert_aircraft('/lab/adsb/aircraft')
convert_airlines('/lab/adsb/airline')

