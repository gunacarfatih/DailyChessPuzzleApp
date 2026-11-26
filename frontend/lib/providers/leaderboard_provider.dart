import 'dart:async';

import 'package:flutter/foundation.dart';

import '../core/services/leaderboard_signalr_service.dart';
import '../features/leaderboard/models/leaderboard_response.dart';
import '../features/leaderboard/services/leaderboard_api_service.dart';

class LeaderboardProvider extends ChangeNotifier {
  LeaderboardProvider({
    LeaderboardApiService? apiService,
  }) : _apiService =
      apiService ?? LeaderboardApiService();

  final LeaderboardApiService _apiService;

  static const int _leaderboardLimit = 10;

  static const List<String> _periods = [
    'daily',
    'weekly',
    'monthly',
    'alltime',
  ];

  static const Duration _refreshDelay =
  Duration(milliseconds: 300);

  final Map<String, Map<String, LeaderboardResponse>>
  _leaderboards = {};

  final Set<String> _loadingKeys = {};

  final Map<String, String> _errors = {};

  final Map<String, Future<void>>
  _inFlightRequests = {};

  final Set<String> _pendingRefreshes = {};

  final Set<String> _dirtyCountries = {};

  final Set<String> _knownCountries = {''};

  Timer? _refreshTimer;

  int _requestGeneration = 0;

  bool _disposed = false;

  String _getCountryKey(String? country) {
    return country?.trim().toUpperCase() ?? '';
  }

  String? _getApiCountry(String countryKey) {
    return countryKey.isEmpty ? null : countryKey;
  }

  void _notifySafely() {
    if (!_disposed) {
      notifyListeners();
    }
  }

  LeaderboardResponse? leaderboardFor(
      String? country, {
        String period = 'all',
      }) {
    return _leaderboards[_getCountryKey(country)]
    ?[period];
  }

  bool isLoadingFor(
      String? country, {
        String period = 'all',
      }) {
    return _loadingKeys.contains(
      _getCountryKey(country),
    );
  }

  String? errorFor(
      String? country, {
        String period = 'all',
      }) {
    return _errors[_getCountryKey(country)];
  }

  void clearForAccountChange() {
    _requestGeneration++;

    _refreshTimer?.cancel();
    _refreshTimer = null;

    _leaderboards.clear();
    _loadingKeys.clear();
    _errors.clear();
    _inFlightRequests.clear();
    _pendingRefreshes.clear();
    _dirtyCountries.clear();

    _knownCountries
      ..clear()
      ..add('');

    _notifySafely();
  }

  Future<void> loadLeaderboard({
    String? country,
    String period = 'all',
    bool forceRefresh = false,
  }) {
    if (_disposed) {
      return Future<void>.value();
    }

    final countryKey = _getCountryKey(country);

    _knownCountries.add(countryKey);

    final existingRequest =
    _inFlightRequests[countryKey];

    if (existingRequest != null) {
      if (forceRefresh) {
        _pendingRefreshes.add(countryKey);
      }

      return existingRequest;
    }

    final hasCachedData =
        _leaderboards[countryKey]
            ?.containsKey(period) ==
            true;

    if (!forceRefresh && hasCachedData) {
      return Future<void>.value();
    }

    final generation = _requestGeneration;

    final completer = Completer<void>();

    _inFlightRequests[countryKey] =
        completer.future;

    unawaited(
      _runRequest(
        countryKey: countryKey,
        generation: generation,
        completer: completer,
      ),
    );

    return completer.future;
  }

  Future<void> _runRequest({
    required String countryKey,
    required int generation,
    required Completer<void> completer,
  }) async {
    _loadingKeys.add(countryKey);
    _errors.remove(countryKey);

    try {
      _notifySafely();

      do {
        if (_disposed ||
            generation != _requestGeneration) {
          return;
        }

        _pendingRefreshes.remove(countryKey);

        await _loadCountry(
          countryKey: countryKey,
          generation: generation,
        );
      } while (
      !_disposed &&
          generation == _requestGeneration &&
          _pendingRefreshes.contains(countryKey));
    } finally {
      if (!_disposed &&
          generation == _requestGeneration) {
        _loadingKeys.remove(countryKey);

        _inFlightRequests.remove(countryKey);

        _pendingRefreshes.remove(countryKey);

        _notifySafely();
      }

      if (!completer.isCompleted) {
        completer.complete();
      }
    }
  }

  Future<void> _loadCountry({
    required String countryKey,
    required int generation,
  }) async {
    try {
      final result =
      await _apiService.getLeaderboard(
        country: _getApiCountry(countryKey),
        limit: _leaderboardLimit,
        period: 'all',
      );

      if (_disposed ||
          generation != _requestGeneration) {
        return;
      }

      if (result.success && result.data != null) {
        final data = result.data!;

        final countryLeaderboards =
        <String, LeaderboardResponse>{
          'all': data,
        };

        for (final period in _periods) {
          countryLeaderboards[period] = data;
        }

        _leaderboards[countryKey] =
            countryLeaderboards;

        _errors.remove(countryKey);
      } else {
        _errors[countryKey] =
        result.message?.isNotEmpty == true
            ? result.message!
            : 'Liderlik tablosu yüklenemedi.';
      }
    } catch (error, stackTrace) {
      if (_disposed ||
          generation != _requestGeneration) {
        return;
      }

      _errors[countryKey] =
      'Liderlik tablosu yüklenirken '
          'bir hata oluştu.';

      if (kDebugMode) {
        debugPrint(
          'Leaderboard yükleme hatası: $error',
        );

        debugPrintStack(
          stackTrace: stackTrace,
        );
      }
    }
  }

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

  void handleLeaderboardEvent(
      LeaderboardUpdateEvent event,
      ) {
    if (_disposed) {
      return;
    }

    if (!_periods.contains(event.period)) {
      return;
    }

    if (event.globalChanged) {
      _markCountryDirty('');
    }

    final countryKey =
    _getCountryKey(event.countryCode);

    if (countryKey.isNotEmpty) {
      _markCountryDirty(countryKey);
    }

    _scheduleRefresh();
  }

  void _markCountryDirty(String countryKey) {
    _knownCountries.add(countryKey);

    _dirtyCountries.add(countryKey);

    if (_inFlightRequests.containsKey(countryKey)) {
      _pendingRefreshes.add(countryKey);
    }
  }

  void _scheduleRefresh() {
    if (_disposed || _dirtyCountries.isEmpty) {
      return;
    }

    if (_refreshTimer?.isActive == true) {
      return;
    }

    final generation = _requestGeneration;

    _refreshTimer = Timer(
      _refreshDelay,
          () {
        _refreshTimer = null;

        if (_disposed ||
            generation != _requestGeneration) {
          return;
        }

        unawaited(_flushDirtyCountries());
      },
    );
  }

  Future<void> _flushDirtyCountries() async {
    if (_disposed) {
      return;
    }

    final countries =
    List<String>.from(_dirtyCountries);

    _dirtyCountries.clear();

    await Future.wait(
      countries.map(
            (countryKey) => refreshLeaderboard(
          country: _getApiCountry(countryKey),
        ),
      ),
    );
  }

  Future<void> refreshAllKnownLeaderboards({
    String? country,
  }) async {
    if (_disposed) {
      return;
    }

    _knownCountries.add('');

    if (country != null) {
      _knownCountries.add(
        _getCountryKey(country),
      );
    }

    final countries =
    List<String>.from(_knownCountries);

    await Future.wait(
      countries.map(
            (countryKey) => refreshLeaderboard(
          country: _getApiCountry(countryKey),
        ),
      ),
    );
  }

  @override
  void dispose() {
    _disposed = true;

    _requestGeneration++;

    _refreshTimer?.cancel();

    _pendingRefreshes.clear();
    _dirtyCountries.clear();
    _inFlightRequests.clear();

    super.dispose();
  }
}