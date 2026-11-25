import 'dart:async';

/// Resolves a device country at most once per throttle window.
///
/// Location failures return null and never affect the user's current country.
class CountrySyncService {
  CountrySyncService({
    required this.countryLookup,
    this.throttle = const Duration(minutes: 15),
    DateTime Function()? clock,
  }) : _clock = clock ?? DateTime.now;

  final Future<String?> Function() countryLookup;
  final Duration throttle;
  final DateTime Function() _clock;

  Future<String?>? _inFlight;
  DateTime? _lastAttempt;

  void reset() {
    _inFlight = null;
    _lastAttempt = null;
  }

  Future<String?> resolve({bool force = false}) {
    final current = _inFlight;
    if (current != null) return current;

    final lastAttempt = _lastAttempt;
    if (!force &&
        lastAttempt != null &&
        _clock().difference(lastAttempt) < throttle) {
      return Future<String?>.value(null);
    }

    _lastAttempt = _clock();
    final operation = _lookupSafely();
    _inFlight = operation;
    return operation.whenComplete(() {
      if (identical(_inFlight, operation)) _inFlight = null;
    });
  }

  Future<String?> _lookupSafely() async {
    try {
      final value = (await countryLookup())?.trim().toUpperCase();
      if (value == null || !RegExp(r'^[A-Z]{2}$').hasMatch(value)) {
        return null;
      }
      return value;
    } catch (_) {
      return null;
    }
  }
}
