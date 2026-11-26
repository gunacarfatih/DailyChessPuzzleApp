import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:daily_chess_puzzle/core/services/country_sync_service.dart';

void main() {
  group('CountrySyncService', () {
    test('normalizes country codes and throttles later lookups', () async {
      var calls = 0;
      var now = DateTime.utc(2026, 1, 1);
      final service = CountrySyncService(
        countryLookup: () async {
          calls++;
          return ' tr ';
        },
        clock: () => now,
      );

      expect(await service.resolve(), 'TR');
      expect(await service.resolve(), isNull);
      expect(calls, 1);

      now = now.add(const Duration(minutes: 16));
      expect(await service.resolve(), 'TR');
      expect(calls, 2);
    });

    test('coalesces concurrent lookups into one operation', () async {
      final result = Completer<String?>();
      var calls = 0;
      final service = CountrySyncService(
        countryLookup: () {
          calls++;
          return result.future;
        },
      );

      final first = service.resolve();
      final second = service.resolve();
      result.complete('DE');

      expect(await first, 'DE');
      expect(await second, 'DE');
      expect(calls, 1);
    });

    test('ignores invalid country values and lookup failures', () async {
      final invalid = CountrySyncService(countryLookup: () async => 'TUR');
      final failure = CountrySyncService(
        countryLookup: () => Future<String?>.error(Exception('GPS off')),
      );

      expect(await invalid.resolve(), isNull);
      expect(await failure.resolve(), isNull);
    });
  });
}
