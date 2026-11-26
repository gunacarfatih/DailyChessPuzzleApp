import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../constants/api_constants.dart';
import '../storage/auth_storage.dart';

class LeaderboardUpdateEvent {
  final String period;
  final String periodKey;
  final String? countryCode;
  final bool globalChanged;

  const LeaderboardUpdateEvent({
    required this.period,
    required this.periodKey,
    required this.countryCode,
    required this.globalChanged,
  });

  factory LeaderboardUpdateEvent.fromJson(
      Map<String, dynamic> json,
      ) {
    final country = json['countryCode']
        ?.toString()
        .trim()
        .toUpperCase();

    return LeaderboardUpdateEvent(
      period: json['period']
          .toString()
          .trim()
          .toLowerCase(),
      periodKey: json['periodKey'].toString(),
      countryCode:
      country == null || country.isEmpty
          ? null
          : country,
      globalChanged:
      json['globalChanged'] == true ||
          country == null ||
          country.isEmpty,
    );
  }
}

class LeaderboardSignalRService {
  HubConnection? _connection;

  Future<void> _operationQueue = Future<void>.value();

  String? _subscribedCountry;

  static const Set<String> _supportedPeriods = {
    'daily',
    'weekly',
    'monthly',
    'alltime',
  };

  bool get isConnected =>
      _connection?.state ==
          HubConnectionState.Connected;

  // Geriye dönük uyumluluk.
  VoidCallback? onLeaderboardUpdated;

  // Yeni detaylı bildirim callback'i.
  ValueChanged<LeaderboardUpdateEvent>?
  onLeaderboardEvent;

  // Bağlantı ve abonelikler yeniden kurulduğunda.
  VoidCallback? onConnectionRestored;

  Future<void> _enqueue(
      Future<void> Function() operation,
      ) {
    final next = _operationQueue.then(
          (_) => operation(),
    );

    _operationQueue = next.then<void>(
          (_) {},
      onError: (Object error, StackTrace stackTrace) {
        _debugLog('SignalR işlem hatası: $error');
        _debugLog(stackTrace);
      },
    );

    return next;
  }

  Future<void> connect() {
    return _enqueue(_connectInternal);
  }

  Future<void> _connectInternal() async {
    if (isConnected) {
      await _joinAllLeaderboards(_connection!);
      return;
    }

    if (_connection?.state ==
        HubConnectionState.Reconnecting) {
      return;
    }

    await _disconnectInternal();

    final hubUrl =
        '${ApiConstants.baseUrl}/hubs/leaderboard';

    final connection = HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

    _connection = connection;

    connection.on(
      'LeaderboardUpdated',
          (arguments) {
        if (!identical(_connection, connection)) {
          return;
        }

        _handleLeaderboardUpdated(arguments);
      },
    );

    connection.onclose(({error}) {
      if (!identical(_connection, connection)) {
        return;
      }

      _subscribedCountry = null;

      _debugLog(
        'SignalR bağlantısı kapandı: $error',
      );
    });

    connection.onreconnected(({connectionId}) {
      if (!identical(_connection, connection)) {
        return;
      }

      _debugLog(
        'SignalR yeniden bağlandı: $connectionId',
      );

      unawaited(
        _enqueue(() async {
          if (!identical(_connection, connection)) {
            return;
          }

          try {
            _subscribedCountry = null;

            await _joinAllLeaderboards(connection);

            onConnectionRestored?.call();
          } catch (error, stackTrace) {
            _debugLog(
              'SignalR abonelik yenileme hatası: $error',
            );

            _debugLog(stackTrace);
          }
        }),
      );
    });

    try {
      await connection.start();

      await _joinAllLeaderboards(connection);

      _debugLog(
        'SignalR bağlantısı ve abonelikleri başarılı.',
      );
    } catch (error, stackTrace) {
      _debugLog(
        'SignalR bağlantı hatası: $error',
      );

      _debugLog(stackTrace);

      await _disconnectInternal();
    }
  }

  void _handleLeaderboardUpdated(
      List<Object?>? arguments,
      ) {
    if (arguments == null || arguments.isEmpty) {
      return;
    }

    final payload = arguments.first;

    if (payload is! Map) {
      _debugLog(
        'SignalR: Geçersiz bildirim içeriği.',
      );
      return;
    }

    try {
      final event = LeaderboardUpdateEvent.fromJson(
        Map<String, dynamic>.from(payload),
      );

      if (!_supportedPeriods.contains(event.period)) {
        return;
      }

      if (event.periodKey.isEmpty ||
          event.periodKey == 'null') {
        return;
      }

      _debugLog(
        'SignalR LeaderboardUpdated: '
            'period=${event.period}, '
            'periodKey=${event.periodKey}, '
            'country=${event.countryCode}, '
            'globalChanged=${event.globalChanged}',
      );

      final eventCallback = onLeaderboardEvent;

      if (eventCallback != null) {
        eventCallback(event);
      } else {
        onLeaderboardUpdated?.call();
      }
    } catch (error, stackTrace) {
      _debugLog(
        'SignalR bildirim işleme hatası: $error',
      );

      _debugLog(stackTrace);
    }
  }

  Future<void> _joinAllLeaderboards(
      HubConnection connection,
      ) async {
    if (!identical(_connection, connection) ||
        connection.state !=
            HubConnectionState.Connected) {
      return;
    }

    final storedCountry =
    await AuthStorage.instance.getCountryCode();

    final countryCode =
        storedCountry?.trim().toUpperCase() ?? '';

    final previousCountry = _subscribedCountry;

    if (previousCountry == countryCode) {
      return;
    }

    if (previousCountry != null) {
      await connection.invoke(
        'LeaveAllLeaderboards',
        args: <Object>[previousCountry],
      );

      _subscribedCountry = null;
    }

    await connection.invoke(
      'JoinAllLeaderboards',
      args: <Object>[countryCode],
    );

    _subscribedCountry = countryCode;

    _debugLog(
      'SignalR: Tüm liderlik tablolarına abone olundu. '
          'country=$countryCode',
    );
  }

  Future<void> refreshSubscriptions() {
    return _enqueue(() async {
      final connection = _connection;

      if (connection == null || !isConnected) {
        return;
      }

      await _joinAllLeaderboards(connection);
    });
  }

  Future<void> disconnect() {
    return _enqueue(_disconnectInternal);
  }

  Future<void> _disconnectInternal() async {
    final connection = _connection;

    _connection = null;
    _subscribedCountry = null;

    if (connection == null) {
      return;
    }

    try {
      await connection.stop();

      _debugLog(
        'SignalR bağlantısı kapatıldı.',
      );
    } catch (error) {
      _debugLog(
        'SignalR bağlantısı kapatma hatası: $error',
      );
    }
  }
}

void _debugLog(Object? message) {
  if (kDebugMode) {
    debugPrint(message.toString());
  }
}