import 'package:geocoding/geocoding.dart';
import 'package:geolocator/geolocator.dart';

import '../storage/country_code_storage.dart';

class CountryInfo {
  final String code;
  final String name;

  const CountryInfo({required this.code, required this.name});
}

class LocationService {
  static const _serviceTimeout = Duration(seconds: 5);
  static const _permissionTimeout = Duration(seconds: 30);
  static const _positionTimeout = Duration(seconds: 15);
  static const _geocodingTimeout = Duration(seconds: 10);
  static const _storageTimeout = Duration(seconds: 5);

  final CountryCodeStorage _countryCodeStorage = CountryCodeStorage();

  // Defer plugin construction until location lookup is requested. The service
  // should remain safe to create before platform plugins are available.
  late final Geocoding _geocoding = Geocoding();

  /// Ülkenin ISO kodunu döndürür.
  /// Örnek: TR, DE, US
  Future<String?> getCountryCode() async {
    final countryInfo = await getCountryInfo();
    return countryInfo?.code;
  }

  /// Ülkenin ISO kodunu ve adını döndürür.
  Future<CountryInfo?> getCountryInfo() async {
    try {
      // Always resolve the current device location. The persisted value is
      // only a UI/cache fallback; using it as the lookup result would prevent
      // the account country from ever following travel to another country.
      final serviceEnabled = await Geolocator.isLocationServiceEnabled().timeout(_serviceTimeout);

      if (!serviceEnabled) return null;

      // Konum iznini kontrol et.
      LocationPermission permission = await Geolocator.checkPermission().timeout(_serviceTimeout);

      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission().timeout(_permissionTimeout);
      }

      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        return null;
      }

      // Mevcut konumu al.
      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.low,
          timeLimit: _positionTimeout,
        ),
      ).timeout(_positionTimeout);

      // Koordinatlardan ülke bilgisini bul.
      final placemarks = await _geocoding.placemarkFromCoordinates(
        position.latitude,
        position.longitude,
      ).timeout(_geocodingTimeout);

      if (placemarks.isEmpty) return null;

      final placemark = placemarks.first;

      final countryCode = placemark.isoCountryCode?.trim().toUpperCase();

      final countryName = placemark.country?.trim();

      if (countryCode == null ||
          !RegExp(r'^[A-Z]{2}$').hasMatch(countryCode) ||
          countryName == null ||
          countryName.isEmpty) {
        return null;
      }

      // Ülke kodunu ve adını cihazda sakla.
      try {
        await _countryCodeStorage.saveCountryCode(countryCode).timeout(_storageTimeout);
        await _countryCodeStorage.saveCountryName(countryName).timeout(_storageTimeout);
      } catch (_) {
        // Önbellek hatası geçerli konum sonucunu engellememeli.
      }

      return CountryInfo(code: countryCode, name: countryName);
    } catch (e) {
      // Konum hatası uygulamanın çalışmasını engellememeli.
      return null;
    }
  }
}
