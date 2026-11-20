
import 'package:flutter/foundation.dart';

import '../features/leaderboard/models/leaderboard_response.dart';
import '../features/leaderboard/services/leaderboard_api_service.dart';

class LeaderboardProvider extends ChangeNotifier {
  LeaderboardProvider({LeaderboardApiService? apiService})
      : _apiService = apiService ?? LeaderboardApiService();

  final LeaderboardApiService _apiService;

  // Her ülkenin leaderboard verisini ayrı saklar.
  // Boş String global sıralamayı temsil eder.
  final Map<String, Map<String, LeaderboardResponse>> _leaderboards = {};

  // Her ülkenin yüklenme durumunu ayrı saklar.
  final Set<String> _loadingKeys = {};

  // Her ülkenin hata mesajını ayrı saklar.
  final Map<String, String> _errors = {};

  // Aynı ülke için devam eden istekleri takip eder.
  final Map<String, Future<void>> _inFlightRequests = {};

  static const int _leaderboardLimit = 10;

  // Hesap değiştiğinde artırılır.
  // Eski nesle ait isteklerin sonuçlarını kaydetmesini engeller.
  int _requestGeneration = 0;

  // Ülke kodunu standart bir anahtara dönüştürür.
  String _getCountryKey(String? country) {
    return country?.trim().toUpperCase() ?? '';
  }

  // İstenen ülkenin leaderboard verisini döndürür.
  String _getRequestKey(String? country, String period) =>
      '${_getCountryKey(country)}|$period';

  LeaderboardResponse? leaderboardFor(String? country, {String period = 'all'}) {
    return _leaderboards[_getCountryKey(country)]?[period];
  }

  // İstenen ülkenin yüklenme durumunu döndürür.
  bool isLoadingFor(String? country, {String period = 'all'}) {
    return _loadingKeys.contains(_getRequestKey(country, period));
  }

  // İstenen ülkenin hata mesajını döndürür.
  String? errorFor(String? country, {String period = 'all'}) {
    return _errors[_getRequestKey(country, period)];
  }

  // Hesap değiştiğinde bütün leaderboard verilerini temizler.
  void clearForAccountChange() {
    // Önceki isteklerin sonuçlarını geçersiz hâle getir.
    _requestGeneration++;

    // Eski hesaba ait verileri ve istek kayıtlarını temizle.
    _leaderboards.clear();
    _loadingKeys.clear();
    _errors.clear();
    _inFlightRequests.clear();

    notifyListeners();
  }

  // Leaderboard verisini yükler.
  //
  // forceRefresh false ise mevcut veri tekrar kullanılır.
  // forceRefresh true ise API'den yeniden veri istenir.
  Future<void> loadLeaderboard({
    String? country,
    String period = 'all',
    bool forceRefresh = false,
  }) {
    final countryKey = _getCountryKey(country);
    final key = _getRequestKey(country, period);

    // Aynı ülke için devam eden istek varsa mevcut isteği kullan.
    final existingRequest = _inFlightRequests[key];

    if (existingRequest != null) {
      return existingRequest;
    }

    // Bellekte veri varsa gereksiz API isteği yapma.
    if (!forceRefresh && _leaderboards[countryKey]?.containsKey(period) == true) {
      return Future<void>.value();
    }

    // İsteğin başladığı hesap neslini sakla.
    final generation = _requestGeneration;

    final request = _loadLeaderboard(
      key: key,
      countryKey: countryKey,
      country: country,
      period: period,
      generation: generation,
    );

    _inFlightRequests[key] = request;

    return request;
  }

  // API isteğini gerçekleştirir.
  Future<void> _loadLeaderboard({
    required String key,
    required String countryKey,
    required int generation,
    required String period,
    String? country,
  }) async {
    _loadingKeys.add(key);
    _errors.remove(key);

    notifyListeners();

    try {
      // Kullanıcı kimliği JWT üzerinden belirlenir.
      // API isteğinde ClientId gönderilmez.
      final result = await _apiService.getLeaderboard(
        country: country,
        limit: _leaderboardLimit,
        period: period,
      );

      // Hesap değişmişse eski isteğin sonucunu kullanma.
      if (generation != _requestGeneration) {
        return;
      }

      if (result.success && result.data != null) {
        final countryLeaderboards = _leaderboards.putIfAbsent(
          countryKey,
          () => <String, LeaderboardResponse>{},
        );
        if (period == 'all') {
          for (final value in const ['daily', 'weekly', 'monthly', 'alltime']) {
            countryLeaderboards[value] = result.data!;
          }
        } else {
          countryLeaderboards[period] = result.data!;
        }
      } else {
        _errors[key] = result.message?.isNotEmpty == true
            ? result.message!
            : 'Liderlik tablosu yüklenemedi.';
      }
    } catch (error) {
      // Eski hesaba ait hata bilgisini de yeni oturuma taşıma.
      if (generation == _requestGeneration) {
        _errors[key] =
        'Liderlik tablosu yüklenirken bir hata oluştu.';

        if (kDebugMode) {
          debugPrint('Leaderboard yükleme hatası: $error');
        }
      }
    } finally {
      // Yalnızca hâlâ geçerli olan istek kendi durumunu temizleyebilir.
      // Böylece eski istek yeni isteğin loading durumunu silemez.
      if (generation == _requestGeneration) {
        _loadingKeys.remove(key);
        _inFlightRequests.remove(key);

        notifyListeners();
      }
    }
  }

  // Mevcut veriyi zorla yeniler.
  Future<void> refreshLeaderboard({
    String? country,
    String period = 'all',
  }) {
    return loadLeaderboard(
      country: country,
      period: period,
      forceRefresh: true,
    );
  }
}
