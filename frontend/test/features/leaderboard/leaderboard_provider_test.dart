
import 'dart:async';

import 'package:daily_chess_puzzle/core/models/api_response.dart';
import 'package:daily_chess_puzzle/features/leaderboard/models/leaderboard_period.dart';
import 'package:daily_chess_puzzle/features/leaderboard/models/leaderboard_response.dart';
import 'package:daily_chess_puzzle/features/leaderboard/services/leaderboard_api_service.dart';
import 'package:daily_chess_puzzle/providers/leaderboard_provider.dart';
import 'package:flutter_test/flutter_test.dart';

const _periods = [
  'daily',
  'weekly',
  'monthly',
  'alltime',
];

void main() {
  test(
    'caches all periods with a single request and clears data on account change',
        () async {
      final service = _FakeLeaderboardApiService();
      final provider = LeaderboardProvider(apiService: service);

      addTearDown(provider.dispose);

      await provider.loadLeaderboard(
        country: 'tr',
        period: 'daily',
      );

      await provider.loadLeaderboard(
        country: 'TR',
        period: 'daily',
      );

      await provider.loadLeaderboard(
        country: 'TR',
        period: 'weekly',
      );

      expect(service.requestedPeriods, ['all']);
      expect(service.requestedCountries, ['TR']);

      for (final period in _periods) {
        expect(
          provider.leaderboardFor('TR', period: period),
          isNotNull,
        );
      }

      expect(
        provider.leaderboardFor('tr', period: 'daily'),
        isNotNull,
      );

      provider.clearForAccountChange();

      for (final period in _periods) {
        expect(
          provider.leaderboardFor('TR', period: period),
          isNull,
        );
      }

      await provider.loadLeaderboard(
        country: 'TR',
        period: 'daily',
      );

      expect(service.requestedPeriods, ['all', 'all']);
      expect(service.requestedCountries, ['TR', 'TR']);
    },
  );

  test(
    'deduplicates concurrent requests across periods for the same country',
        () async {
      final completer =
      Completer<ApiResponse<LeaderboardResponse>>();

      final service = _FakeLeaderboardApiService(
        completer: completer,
      );

      final provider = LeaderboardProvider(apiService: service);

      addTearDown(provider.dispose);

      final first = provider.loadLeaderboard(
        country: 'US',
        period: 'monthly',
      );

      final second = provider.loadLeaderboard(
        country: 'US',
        period: 'monthly',
      );

      final third = provider.loadLeaderboard(
        country: 'us',
        period: 'weekly',
      );

      expect(service.requestedPeriods, ['all']);
      expect(service.requestedCountries, ['US']);

      expect(identical(first, second), isTrue);
      expect(identical(first, third), isTrue);

      completer.complete(_successResponse());

      await Future.wait([first, second, third]);

      for (final period in _periods) {
        expect(
          provider.isLoadingFor('US', period: period),
          isFalse,
        );

        expect(
          provider.leaderboardFor('US', period: period),
          isNotNull,
        );
      }
    },
  );

  test(
    'stores API failure across all periods for the requested country',
        () async {
      final service = _FakeLeaderboardApiService(
        response: const ApiResponse<LeaderboardResponse>(
          success: false,
          message: 'Leaderboard unavailable',
        ),
      );

      final provider = LeaderboardProvider(apiService: service);

      addTearDown(provider.dispose);

      await provider.loadLeaderboard(period: 'alltime');

      expect(service.requestedPeriods, ['all']);

      for (final period in _periods) {
        expect(
          provider.errorFor(null, period: period),
          'Leaderboard unavailable',
        );

        expect(
          provider.isLoadingFor(null, period: period),
          isFalse,
        );
      }
    },
  );

  test(
    'keeps global and country caches independent',
        () async {
      final service = _FakeLeaderboardApiService();
      final provider = LeaderboardProvider(apiService: service);

      addTearDown(provider.dispose);

      await provider.loadLeaderboard(period: 'daily');

      expect(
        provider.leaderboardFor(null, period: 'daily'),
        isNotNull,
      );

      expect(
        provider.leaderboardFor('TR', period: 'daily'),
        isNull,
      );

      await provider.loadLeaderboard(
        country: 'TR',
        period: 'weekly',
      );

      await provider.loadLeaderboard(
        country: 'US',
        period: 'monthly',
      );

      expect(
        service.requestedCountries,
        [null, 'TR', 'US'],
      );

      expect(
        service.requestedPeriods,
        ['all', 'all', 'all'],
      );

      for (final period in _periods) {
        expect(
          provider.leaderboardFor(null, period: period),
          isNotNull,
        );

        expect(
          provider.leaderboardFor('TR', period: period),
          isNotNull,
        );

        expect(
          provider.leaderboardFor('US', period: period),
          isNotNull,
        );
      }

      provider.clearForAccountChange();

      for (final period in _periods) {
        expect(
          provider.leaderboardFor(null, period: period),
          isNull,
        );

        expect(
          provider.leaderboardFor('TR', period: period),
          isNull,
        );

        expect(
          provider.leaderboardFor('US', period: period),
          isNull,
        );
      }
    },
  );

  test(
    'does not send additional requests when switching cached periods',
        () async {
      final service = _FakeLeaderboardApiService();
      final provider = LeaderboardProvider(apiService: service);

      addTearDown(provider.dispose);

      for (final period in _periods) {
        await provider.loadLeaderboard(
          country: 'TR',
          period: period,
        );
      }

      expect(service.requestedPeriods, ['all']);
      expect(service.requestedCountries, ['TR']);
    },
  );

  test(
    'force refresh requests all periods again',
        () async {
      final service = _FakeLeaderboardApiService();
      final provider = LeaderboardProvider(apiService: service);

      addTearDown(provider.dispose);

      await provider.loadLeaderboard(
        country: 'TR',
        period: 'daily',
      );

      await provider.refreshLeaderboard(
        country: 'TR',
        period: 'weekly',
      );

      expect(service.requestedPeriods, ['all', 'all']);
      expect(service.requestedCountries, ['TR', 'TR']);

      for (final period in _periods) {
        expect(
          provider.leaderboardFor('TR', period: period),
          isNotNull,
        );
      }
    },
  );

  test(
    'ignores stale responses after account change',
        () async {
      final completer =
      Completer<ApiResponse<LeaderboardResponse>>();

      final service = _FakeLeaderboardApiService(
        completer: completer,
      );

      final provider = LeaderboardProvider(apiService: service);

      addTearDown(provider.dispose);

      final pending = provider.loadLeaderboard(
        country: 'TR',
        period: 'daily',
      );

      provider.clearForAccountChange();

      completer.complete(_successResponse());

      await pending;

      for (final period in _periods) {
        expect(
          provider.leaderboardFor('TR', period: period),
          isNull,
        );

        expect(
          provider.isLoadingFor('TR', period: period),
          isFalse,
        );
      }
    },
  );
}

ApiResponse<LeaderboardResponse> _successResponse() =>
    const ApiResponse<LeaderboardResponse>(
      success: true,
      data: LeaderboardResponse(
        daily: LeaderboardPeriod(period: 'daily'),
        weekly: LeaderboardPeriod(period: 'weekly'),
        monthly: LeaderboardPeriod(period: 'monthly'),
        allTime: LeaderboardPeriod(period: 'alltime'),
      ),
    );

class _FakeLeaderboardApiService
    implements LeaderboardApiService {
  _FakeLeaderboardApiService({
    this.completer,
    this.response,
  });

  final Completer<ApiResponse<LeaderboardResponse>>? completer;
  final ApiResponse<LeaderboardResponse>? response;

  final List<String> requestedPeriods = [];
  final List<String?> requestedCountries = [];

  @override
  Future<ApiResponse<LeaderboardResponse>> getLeaderboard({
    String? country,
    int limit = 10,
    DateTime? startDate,
    DateTime? endDate,
    String? period,
  }) {
    requestedPeriods.add(period ?? 'all');
    requestedCountries.add(country);

    if (completer != null) {
      return completer!.future;
    }

    return Future.value(
      response ?? _successResponse(),
    );
  }
}
