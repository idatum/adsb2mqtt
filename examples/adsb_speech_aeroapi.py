import logging
import json
import os
import http.client
from base64 import b64encode

class AdsbSpeech:
    def __init__(self, 
                 log=logging.getLogger('adsb_speech'),
                 local_city="",
                 skip_general_aviation=False,
                 aeroapi_user=None,
                 aeroapi_key=None,
                 aircraft_dir=None,
                 cache_dir='/var/tmp/flights'):
        self.log = log
        self.flight_origin_dest_type = {}
        self.exclude_icao = set()
        self.unknown_origin = 'Unknown'
        self._local_city = local_city
        self._aeroapi_user = aeroapi_user
        self._aeroapi_key = aeroapi_key
        self._aircraft_dir = aircraft_dir
        self._cache_dir = cache_dir
        self._skip_general_aviation = skip_general_aviation

    @property
    def local_city(self):
        return self._local_city
    
    @local_city.setter
    def local_city(self, value):
        self._local_city = value

    @property
    def skip_general_aviation(self):
        return self._skip_general_aviation
    
    @skip_general_aviation.setter
    def skip_general_aviation(self, value):
        self._skip_general_aviation = value

    def get_icao(self, flight):
        return flight['icao']
    
    def get_nautical_miles(self, flight):
        return flight['nm']
    
    def get_latitude(self, flight):
        return flight['lat']
    
    def get_flight_text(self, flight):
        icao = self.get_icao(flight)
        ident = flight['flt']
        dir_deg = round(flight['dir'], 0)
        dir_deg = 0 if dir_deg == 360 else dir_deg
        heading = "".join([n + ' ' for n in str(dir_deg)])
        text0 = ''
        origin, dest, type = (None, None, None)
        if ident[0] != 'N': 
            origin, dest, type = self._get_flight_origin_dest_type(ident)
        if origin and (origin == self._local_city or origin == self.unknown_origin) and dest:
            text0 = f"to {dest}"
        elif origin and (dest == self._local_city or not dest):
            text0 = f"from {origin}"
        elif origin:
            text0 = f"from {origin} to {dest}"
        airline = None
        if not self._skip_general_aviation or ident[0] != 'N':
            airline_code = ident[:3]
            airline = self._get_airline_name(airline_code, short=True)
        text = f"{airline if airline else ident} {type} at {flight['alt']} heading {heading}{text0}"
        if self._skip_general_aviation and ident[0] == 'N':
            # Drop general aviation tail #s
            skip_text = f"Skipping flight {text}"
            self.log.info(skip_text)
            self.exclude_icao.add(icao)
            return None
        self.log.info(f"FLIGHT {text}")
        return text

    def _get_aeroapi_jresult(self, url):
        userPass = f'{self._aeroapi_user}:{self._aeroapi_key}'
        basicAuth = b64encode(userPass.encode()).decode('ascii')
        headers = {'Authorization' : f'Basic {basicAuth}'}
        h = http.client.HTTPSConnection('flightxml.flightaware.com', timeout=1)
        h.request("GET", url, headers=headers)
        resp = h.getresponse().read().decode('utf-8')
        h.close()
        return json.loads(resp)

    def _get_aeroapi_flightex(self, ident):
        self.log.warning(f"READING: AeroAPI flight info for {ident}")
        return self._get_aeroapi_jresult(f'/json/FlightXML2/FlightInfoEx?ident={ident}&howMany=1')

    def _get_aeroapi_aircrafttype(self, aircraft_type):
        self.log.warning(f"READING: AeroAPI aircraft type info for {aircraft_type}")
        return self._get_aeroapi_jresult(f'/json/FlightXML2/AircraftType?type={aircraft_type}')

    def _get_aeroapi_airlineinfo(self, airline_code):
        self.log.warning(f"READING: AeroAPI airline info for {airline_code}")
        return self._get_aeroapi_jresult(f'/json/FlightXML2/AirlineInfo?airlineCode={airline_code}')

    def _write_flightinfoex(self, fn, ident, flightInfo):
        with open(fn, 'w+') as f:
            if 'error' in flightInfo:
                trimmedInfo = {'FlightInfoExResult': {'flights':
                                [{'ident': ident,
                                    'aircrafttype': '',
                                    'origin': '',
                                    'destination': '',
                                    'originName': '',
                                    'originCity': self.unknown_origin,
                                    'destinationName': '',
                                    'destinationCity': self.unknown_origin
                                }]}}
            else:
                flight0 = flightInfo['FlightInfoExResult']['flights'][0]
                trimmedInfo = {'FlightInfoExResult': {'flights':
                                [{'ident': ident,
                                    'aircrafttype': flight0['aircrafttype'] if 'aircrafttype' in flight0 else '',
                                    'origin': flight0['origin'],
                                    'destination': flight0['destination'],
                                    'originName': flight0['originName'],
                                    'originCity': flight0['originCity'],
                                    'destinationName': flight0['destinationName'],
                                    'destinationCity': flight0['destinationCity']
                                }]}}
            f.write(json.dumps(trimmedInfo))

    def _get_aircraft_type_name(self, aircrafttype):
        fn = os.path.join(self._aircraft_dir, f'aircraft/{aircrafttype}.json')
        jaircraft = None
        if os.path.isfile(fn):
            with open(fn) as f:
                aircraft = f.readline().strip()
                if not aircraft:
                    return None
                jaircraft = json.loads(aircraft)
        elif self._aeroapi_key:
            jaircraft = self._get_aeroapi_aircrafttype(aircrafttype)
            print(jaircraft)
            with open(fn, 'w') as fa:
                fa.write(json.dumps(jaircraft))
        else:
            return aircrafttype
        jresult = jaircraft['AircraftTypeResult']
        manufacturer = jresult['manufacturer']
        if not manufacturer:
            return None
        return f"{manufacturer} {jresult['type']}"

    def _get_airline_name(self, airline_code, short=False):
        fn = os.path.join(self._aircraft_dir, f'airline/{airline_code}.json')
        self.log.info(self._aircraft_dir)
        self.log.info(self._aircraft_dir)
        self.log.info(fn)
        jairline = None
        if os.path.isfile(fn):
            with open(fn) as f:
                airline = f.readline().strip()
                if not airline:
                    return None
                jairline = json.loads(airline)
        elif self._aeroapi_key:
            jairline = self._get_aeroapi_airlineinfo(airline_code)
            if not jairline:
                return None
            print(jairline)
            with open(fn, 'w') as fa:
                fa.write(json.dumps(jairline))
        else:
            return None
        if 'AirlineInfoResult' not in jairline:
            return None
        jresult = jairline['AirlineInfoResult']
        shortname = jresult['shortname']
        name = shortname if shortname else jresult['name']
        country = jresult['country']
        if country and not short:
            return f"{name}, {country}"
        else:
            return f"{name}"

    def _parse_origin_dest_type(self, flightInfo, origin_dest_type):
        if 'error' in flightInfo:
            return
        flight = flightInfo['FlightInfoExResult']['flights'][0]
        if 'originCity' in flight:
            origin_dest_type[0] = flight['originCity'].split(',')[0].replace('/', ' ')
        if 'destinationCity' in flight:
            origin_dest_type[1] = flight['destinationCity'].split(',')[0].replace('/', ' ')
        if 'aircrafttype' in flight:
            origin_dest_type[2] = flight['aircrafttype']

    def _get_flight_origin_dest_type(self, ident):
        origin_dest_type = [None, None, None]
        if ident in self.flight_origin_dest_type:
            return self.flight_origin_dest_type[ident]
        fn = os.path.join(self._cache_dir, f'{ident}.json')
        try:
            if os.path.isfile(fn):
                self.log.debug(f'READING: Existing flight info for {ident}')
                with open(fn) as f:
                    flightFile = f.readline()
                    loadedFlight = json.loads(flightFile)
                    self._parse_origin_dest_type(loadedFlight, origin_dest_type)
                self.flight_origin_dest_type[ident] = origin_dest_type
            elif self._aeroapi_key:
                flightInfo = self._get_aeroapi_flightex(ident)
                self.log.info(flightInfo)
                self._parse_origin_dest_type(flightInfo, origin_dest_type)
                self.flight_origin_dest_type[ident] = origin_dest_type
                try:
                    self._write_flightinfoex(fn, ident, flightInfo)
                except Exception as e:
                    self.log.exception(e)
        except Exception as e:
            self.log.exception(e)
        return origin_dest_type
