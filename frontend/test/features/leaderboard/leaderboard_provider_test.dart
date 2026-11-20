import 'dart:async';

import 'package:daily_chess_puzzle/core/models/api_response.dart';
import 'package:daily_chess_puzzle/features/leaderboard/models/leaderboard_period.dart';
import 'package:daily_chess_puzzle/features/leaderboard/models/leaderboard_response.dart';
import 'package:daily_chess_puzzle/features/leaderboard/services/leaderboard_api_service.dart';
import 'package:daily_chess_puzzle/providers/leaderboard_provider.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('caches each period independently and clears data on account change', () async {
    final service = _FakeLeaderboardApiService();
    final provider = LeaderboardProvider(apiService: service);

    await provider.loadLeaderboard(country: 'tr', period: 'daily');
    await provider.loadLeaderboard(country: 'TR', period: 'daily');
    await provider.loadLeaderboard(country: 'TR', period: 'weekly');

    expect(service.requestedPeriods, ['daily', 'weekly']);
    expect(provider.leaderboardFor('tr', period: 'daily'), isNotNull);
    expect(provider.leaderboardFor('TR', period: 'weekly'), isNotNull);
    expect(provider.leaderboardFor('TR', period: 'monthly'), isNull);

    provider.clearForAccountChange();
    expect(provider.leaderboardFor('TR', period: 'daily'), isNull);

    await provider.loadLeaderboard(country: 'TR', period: 'daily');
    expect(service.requestedPeriods, ['daily', 'weekly', 'daily']);
  });

  test('deduplicates concurrent requests for the same country and period', () async {
    final completer = Completer<ApiResponse<LeaderboardResponse>>();
    final service = _FakeLeaderboardApiService(completer: completer);
    final provider = LeaderboardProvider(apiService: service);

    final first = provider.loadLeaderboard(country: 'US', period: 'monthly');
    final second = provider.loadLeaderboard(country: 'US', period: 'monthly');

    expect(service.requestedPeriods, ['monthly']);
    expect(identical(first, second), isTrue);

    completer.complete(_successResponse());
    await Future.wait([first, second]);
    expect(provider.isLoadingFor('US', period: 'monthly'), isFalse);
  });

  test('stores API failure for the requested period', () async {
    final service = _FakeLeaderboardApiService(
      response: const ApiResponse<LeaderboardResponse>(
        success: false,
        message: 'Leaderboard unavailable',
      ),
    );
    final provider = LeaderboardProvider(apiService: service);

    await provider.loadLeaderboard(period: 'alltime');

    expect(provider.errorFor(null, period: 'alltime'), 'Leaderboard unavailable');
    expect(provider.isLoadingFor(null, period: 'alltime'), isFalse);
  });
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

class _FakeLeaderboardApiService implements LeaderboardApiService {
  _FakeLeaderboardApiService({
    this.completer,
    this.response,
  });

  final Completer<ApiResponse<LeaderboardResponse>>? completer;
  final ApiResponse<LeaderboardResponse>? response;
  final List<String> requestedPeriods = [];

  @override
  Future<ApiResponse<LeaderboardResponse>> getLeaderboard({
    String? country,
    int limit = 10,
    DateTime? startDate,
    DateTime? endDate,
    String? period,
  }) {
    requestedPeriods.add(period ?? 'all');
    if (completer != null) return completer!.future;
    return Future.value(response ?? _successResponse());
  }
}
