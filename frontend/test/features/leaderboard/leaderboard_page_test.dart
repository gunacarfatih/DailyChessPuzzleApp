
import 'dart:async';

import 'package:daily_chess_puzzle/core/models/api_response.dart';
import 'package:daily_chess_puzzle/features/leaderboard/models/leaderboard_period.dart';
import 'package:daily_chess_puzzle/features/leaderboard/models/leaderboard_response.dart';
import 'package:daily_chess_puzzle/features/leaderboard/screens/leaderboard_page.dart';
import 'package:daily_chess_puzzle/features/leaderboard/services/leaderboard_api_service.dart';
import 'package:daily_chess_puzzle/providers/leaderboard_provider.dart';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

const _locationChannel = MethodChannel(
  'flutter.baseflow.com/geolocator',
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  testWidgets(
    'loads global leaderboard without waiting for location',
        (tester) async {
      final environment = _TestEnvironment();

      try {
        await environment.open(tester);

        expect(environment.locationRequestCount, 1);
        expect(environment.locationCompleter.isCompleted, isFalse);

        expect(environment.apiService.requestCount, 1);
        expect(environment.apiService.requestedCountries, [null]);
        expect(environment.apiService.requestedPeriods, ['daily']);

        expect(
          find.byType(CircularProgressIndicator),
          findsOneWidget,
        );

        await environment.completeApi(tester);

        // Konum isteği henüz tamamlanmadı.
        expect(
          environment.locationCompleter.isCompleted,
          isFalse,
        );

        // API tamamlandığı için global sıralama açılmalı.
        environment.expectGlobalLeaderboard();
      } finally {
        await environment.close(tester);
      }
    },
  );

  testWidgets(
    'keeps global leaderboard when location service is disabled',
        (tester) async {
      final environment = _TestEnvironment();

      try {
        await environment.open(tester);
        await environment.completeApi(tester);

        environment.expectGlobalLeaderboard();

        environment.locationCompleter.complete(false);

        await _flushFrames(tester);

        environment.expectGlobalLeaderboard();

        expect(environment.apiService.requestCount, 1);
        expect(tester.takeException(), isNull);
      } finally {
        await environment.close(tester);
      }
    },
  );

  testWidgets(
    'keeps global leaderboard when location lookup throws',
        (tester) async {
      final environment = _TestEnvironment();

      try {
        await environment.open(tester);
        await environment.completeApi(tester);

        environment.expectGlobalLeaderboard();

        environment.locationCompleter.completeError(
          PlatformException(
            code: 'LOCATION_ERROR',
            message: 'Location unavailable',
          ),
        );

        await _flushFrames(tester);

        environment.expectGlobalLeaderboard();

        expect(environment.apiService.requestCount, 1);
        expect(tester.takeException(), isNull);
      } finally {
        await environment.close(tester);
      }
    },
  );

  testWidgets(
    'keeps global leaderboard after location service timeout',
        (tester) async {
      final environment = _TestEnvironment();

      try {
        await environment.open(tester);
        await environment.completeApi(tester);

        environment.expectGlobalLeaderboard();

        expect(
          environment.locationCompleter.isCompleted,
          isFalse,
        );

        // LocationService servis kontrolü için
        // 5 saniyelik timeout kullanıyor.
        await tester.pump(
          const Duration(seconds: 6),
        );

        await _flushFrames(tester);

        // Konum servisi cevap vermedi.
        expect(
          environment.locationCompleter.isCompleted,
          isFalse,
        );

        // Timeout global sıralamayı etkilememeli.
        environment.expectGlobalLeaderboard();

        expect(environment.apiService.requestCount, 1);
        expect(tester.takeException(), isNull);
      } finally {
        await environment.close(tester);
      }
    },
  );

  testWidgets(
    'stops loading when API returns empty rankings',
        (tester) async {
      final environment = _TestEnvironment();

      try {
        await environment.open(tester);

        environment.locationCompleter.complete(false);

        await _flushFrames(tester);

        // API henüz tamamlanmadığı için yükleme sürmeli.
        expect(
          find.byType(CircularProgressIndicator),
          findsOneWidget,
        );

        await environment.completeApi(tester);

        environment.expectGlobalLeaderboard();

        expect(
          find.text('Henüz sıralama verisi bulunmuyor.'),
          findsOneWidget,
        );

        expect(
          environment.provider.leaderboardFor(
            null,
            period: 'daily',
          ),
          isNotNull,
        );
      } finally {
        await environment.close(tester);
      }
    },
  );

  testWidgets(
    'shows API error instead of infinite loading',
        (tester) async {
      final environment = _TestEnvironment();

      try {
        await environment.open(tester);

        environment.locationCompleter.complete(false);

        environment.apiCompleter.complete(
          const ApiResponse<LeaderboardResponse>(
            success: false,
            message: 'Leaderboard unavailable',
          ),
        );

        await _flushFrames(tester);

        expect(
          environment.provider.isLoadingFor(
            null,
            period: 'daily',
          ),
          isFalse,
        );

        expect(
          environment.provider.errorFor(
            null,
            period: 'daily',
          ),
          'Leaderboard unavailable',
        );

        expect(
          find.byType(CircularProgressIndicator),
          findsNothing,
        );

        expect(
          find.text('Liderlik tablosu yüklenemedi.'),
          findsOneWidget,
        );

        expect(
          find.text('Leaderboard unavailable'),
          findsOneWidget,
        );

        expect(
          find.text('Tekrar Dene'),
          findsOneWidget,
        );

        expect(tester.takeException(), isNull);
      } finally {
        await environment.close(tester);
      }
    },
  );
}

// Asenkron işlemlerin ve Provider bildirimlerinin
// widget ağacına yansımasını sağlar.
//
// Sonsuz animasyon nedeniyle pumpAndSettle kullanılmaz.
Future<void> _flushFrames(WidgetTester tester) async {
  await tester.pump();
  await tester.pump();
  await tester.pump(
    const Duration(milliseconds: 100),
  );
  await tester.pump();
}

class _TestEnvironment {
  _TestEnvironment() {
    apiService = _FakeLeaderboardApiService(apiCompleter);

    provider = LeaderboardProvider(
      apiService: apiService,
    );
  }

  // Bu nesneler testWidgets içerisinde oluşturulur.
  // Böylece Flutter FakeAsync ortamında çalışırlar.
  final Completer<bool> locationCompleter =
  Completer<bool>();

  final Completer<ApiResponse<LeaderboardResponse>>
  apiCompleter =
  Completer<ApiResponse<LeaderboardResponse>>();

  late final _FakeLeaderboardApiService apiService;
  late final LeaderboardProvider provider;

  int locationRequestCount = 0;

  Future<void> open(WidgetTester tester) async {
    TestDefaultBinaryMessengerBinding
        .instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
      _locationChannel,
          (MethodCall call) async {
        switch (call.method) {
          case 'isLocationServiceEnabled':
            locationRequestCount++;
            return await locationCompleter.future;

          case 'checkPermission':
          case 'requestPermission':
            return 0;

          default:
            return null;
        }
      },
    );

    await tester.pumpWidget(
      ChangeNotifierProvider<LeaderboardProvider>.value(
        value: provider,
        child: const MaterialApp(
          home: LeaderboardPage(),
        ),
      ),
    );

    await _flushFrames(tester);
  }

  Future<void> completeApi(WidgetTester tester) async {
    apiCompleter.complete(_successResponse());

    await _flushFrames(tester);

    // Önce Provider seviyesinde isteğin tamamlandığını
    // doğrula, ardından widget kontrollerine geç.
    expect(
      provider.isLoadingFor(
        null,
        period: 'daily',
      ),
      isFalse,
      reason: 'API tamamlandıktan sonra loading kapanmalı.',
    );

    expect(
      provider.leaderboardFor(
        null,
        period: 'daily',
      ),
      isNotNull,
      reason: 'Başarılı API sonucu Provider içinde saklanmalı.',
    );
  }

  void expectGlobalLeaderboard() {
    expect(
      provider.isLoadingFor(
        null,
        period: 'daily',
      ),
      isFalse,
    );

    expect(
      find.text('En İyi Oyuncular'),
      findsOneWidget,
    );

    expect(
      find.text('Global'),
      findsOneWidget,
    );

    expect(
      find.byType(CircularProgressIndicator),
      findsNothing,
    );
  }

  Future<void> close(WidgetTester tester) async {
    try {
      // Bekleyen işlemleri tamamla.
      if (!locationCompleter.isCompleted) {
        locationCompleter.complete(false);
      }

      if (!apiCompleter.isCompleted) {
        apiCompleter.complete(_successResponse());
      }

      await _flushFrames(tester);

      // LocationService tarafından oluşturulabilecek
      // servis timeout timer'ının temizlenmesini sağla.
      await tester.pump(
        const Duration(seconds: 6),
      );

      await _flushFrames(tester);

      // Provider dispose edilmeden önce widget ağacını kaldır.
      await tester.pumpWidget(
        const SizedBox.shrink(),
      );

      await tester.pump();
    } finally {
      TestDefaultBinaryMessengerBinding
          .instance.defaultBinaryMessenger
          .setMockMethodCallHandler(
        _locationChannel,
        null,
      );

      provider.dispose();
    }
  }
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
  _FakeLeaderboardApiService(this.completer);

  final Completer<ApiResponse<LeaderboardResponse>> completer;

  int requestCount = 0;

  final List<String?> requestedCountries = [];
  final List<String> requestedPeriods = [];

  @override
  Future<ApiResponse<LeaderboardResponse>> getLeaderboard({
    String? country,
    int limit = 10,
    DateTime? startDate,
    DateTime? endDate,
    String? period,
  }) {
    requestCount++;

    requestedCountries.add(country);
    requestedPeriods.add(period ?? 'all');

    return completer.future;
  }
}
