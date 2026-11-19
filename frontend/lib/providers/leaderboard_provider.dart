
import 'package:flutter/foundation.dart';

import '../core/storage/client_id_storage.dart';
import '../features/leaderboard/models/leaderboard_response.dart';
import '../features/leaderboard/services/leaderboard_api_service.dart';

class LeaderboardProvider extends ChangeNotifier {
  final LeaderboardApiService _apiService = LeaderboardApiService();
  final ClientIdStorage _clientIdStorage = ClientIdStorage();

  // Her ülkenin leaderboard verisini ayrı saklar.
  // Boş String global sıralamayı temsil eder.
  final Map<String, LeaderboardResponse> _leaderboards = {};

  // Her ülkenin yüklenme durumunu ayrı saklar.
  final Set<String> _loadingCountries = {};

  // Her ülkenin hata mesajını ayrı saklar.
  final Map<String, String> _errors = {};

  // Aynı ülke için devam eden istekleri takip eder.
  final Map<String, Future<void>> _inFlightRequests = {};

  static const int _leaderboardLimit = 10;

  // Ülke kodunu standart bir anahtara dönüştürür.
  String _getCountryKey(String? country) {
    return country?.trim().toUpperCase() ?? '';
  }

  // İstenen ülkenin leaderboard verisini getirir.
  LeaderboardResponse? leaderboardFor(String? country) {
    final key = _getCountryKey(country);
    return _leaderboards[key];
  }

  // İstenen ülkenin yüklenme durumunu döndürür.
  bool isLoadingFor(String? country) {
    final key = _getCountryKey(country);
    return _loadingCountries.contains(key);
  }

  // İstenen ülkenin hata mesajını döndürür.
  String? errorFor(String? country) {
    final key = _getCountryKey(country);
    return _errors[key];
  }

  // Leaderboard verisini yükler.
  //
  // forceRefresh false ise mevcut veri tekrar kullanılır.
  // forceRefresh true ise API'den yeniden veri istenir.
  Future<void> loadLeaderboard({
    String? country,
    bool forceRefresh = false,
  }) {
    final key = _getCountryKey(country);

    // Aynı ülke için devam eden bir istek varsa
    // yeni istek başlatmak yerine mevcut isteği kullan.
    final existingRequest = _inFlightRequests[key];

    if (existingRequest != null) {
      return existingRequest;
    }

    // Bellekte veri varsa gereksiz API isteği yapma.
    if (!forceRefresh && _leaderboards.containsKey(key)) {
      return Future<void>.value();
    }

    final request = _loadLeaderboard(
      key: key,
      country: country,
    );

    _inFlightRequests[key] = request;

    return request;
  }

  // API isteğini gerçekleştirir.
  Future<void> _loadLeaderboard({
    required String key,
    String? country,
  }) async {
    _loadingCountries.add(key);
    _errors.remove(key);

    notifyListeners();

    try {
      // Kullanıcının ClientId bilgisini al.
      final clientId = await _clientIdStorage.getClientId();

      // Leaderboard API isteğini gerçekleştir.
      final result = await _apiService.getLeaderboard(
        clientId: clientId,
        country: country,
        limit: _leaderboardLimit,
      );

      if (result.success && result.data != null) {
        _leaderboards[key] = result.data!;
      } else {
        _errors[key] = result.message?.isNotEmpty == true
            ? result.message!
            : 'Liderlik tablosu yüklenemedi.';
      }
    } catch (error) {
      _errors[key] = 'Liderlik tablosu yüklenirken bir hata oluştu.';

      debugPrint('Leaderboard yükleme hatası: $error');
    } finally {
      _loadingCountries.remove(key);
      _inFlightRequests.remove(key);

      notifyListeners();
    }
  }

  // Mevcut veriyi zorla yeniler.
  Future<void> refreshLeaderboard({
    String? country,
  }) {
    return loadLeaderboard(
      country: country,
      forceRefresh: true,
    );
  }
}
