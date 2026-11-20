
import 'package:geocoding/geocoding.dart';
import 'package:geolocator/geolocator.dart';

import '../storage/country_code_storage.dart';

class CountryInfo {
  final String code;
  final String name;

  const CountryInfo({
    required this.code,
    required this.name,
  });
}

class LocationService {
  final CountryCodeStorage _countryCodeStorage =
  CountryCodeStorage();

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
      // Önce kayıtlı ülke kodunu kontrol et.
      final savedCountryCode =
      await _countryCodeStorage.getCountryCode();

      if (savedCountryCode != null &&
          savedCountryCode.isNotEmpty) {
        final savedCountryName =
        await _countryCodeStorage.getCountryName();

        if (savedCountryName != null &&
            savedCountryName.isNotEmpty) {
          return CountryInfo(
            code: savedCountryCode.toUpperCase(),
            name: savedCountryName,
          );
        }
      }

      // Konum servisinin açık olup olmadığını kontrol et.
      final serviceEnabled =
      await Geolocator.isLocationServiceEnabled();

      if (!serviceEnabled) return null;

      // Konum iznini kontrol et.
      LocationPermission permission =
      await Geolocator.checkPermission();

      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }

      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        return null;
      }

      // Mevcut konumu al.
      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.low,
          timeLimit: Duration(seconds: 15),
        ),
      );

      // Koordinatlardan ülke bilgisini bul.
      final placemarks =
      await _geocoding.placemarkFromCoordinates(
        position.latitude,
        position.longitude,
      );

      if (placemarks.isEmpty) return null;

      final placemark = placemarks.first;

      final countryCode =
      placemark.isoCountryCode?.toUpperCase();

      final countryName = placemark.country;

      if (countryCode == null ||
          countryCode.isEmpty ||
          countryName == null ||
          countryName.isEmpty) {
        return null;
      }

      // Ülke kodunu ve adını cihazda sakla.
      await _countryCodeStorage.saveCountryCode(countryCode);
      await _countryCodeStorage.saveCountryName(countryName);

      return CountryInfo(
        code: countryCode,
        name: countryName,
      );
    } catch (e) {
      // Konum hatası uygulamanın çalışmasını engellememeli.
      return null;
    }
  }
}
