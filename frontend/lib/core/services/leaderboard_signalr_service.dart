
import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../constants/api_constants.dart';
import '../storage/auth_storage.dart';

class LeaderboardSignalRService {
  HubConnection? _connection;

  bool get isConnected =>
      _connection?.state == HubConnectionState.Connected;

  // Liderlik tablosu güncellendiğinde çalışır.
  VoidCallback? onLeaderboardUpdated;

  // SignalR yeniden bağlandığında çalışır.
  VoidCallback? onConnectionRestored;


  Future<void> connect() async {
    _debugLog('SignalR: connect() çağrıldı.');

    if (isConnected) {
      _debugLog('SignalR: Zaten bağlı.');
      return;
    }

    try {
      final hubUrl = '${ApiConstants.baseUrl}/hubs/leaderboard';

      _debugLog('SignalR: Hub URL: $hubUrl');

      final httpOptions = HttpConnectionOptions(
        accessTokenFactory: () async =>
            await AuthStorage.instance.getAccessToken() ?? '',
      );

      _connection = HubConnectionBuilder()
          .withUrl(hubUrl, options: httpOptions)
          .withAutomaticReconnect()
          .build();

      _connection!.on('LeaderboardUpdated', (arguments) {
        _debugLog('SignalR: LeaderboardUpdated alındı: $arguments');
        onLeaderboardUpdated?.call();
      });

      _connection!.onclose(({error}) {
        _debugLog('SignalR bağlantısı kapandı: $error');
      });

      _connection!.onreconnected(({connectionId}) {
        _debugLog('SignalR yeniden bağlandı: $connectionId');
        onConnectionRestored?.call();
      });

      _debugLog('SignalR: Bağlantı başlatılıyor...');

      await _connection!.start();

      _debugLog('SignalR bağlantısı başarılı.');
    } catch (e, stackTrace) {
      _debugLog('SignalR bağlantı hatası: $e');
      _debugLog('SignalR stack trace: $stackTrace');
    }
  }


  Future<void> disconnect() async {
    final connection = _connection;

    if (connection == null) return;

    _connection = null;

    try {
      await connection.stop();
      _debugLog('SignalR bağlantısı kapatıldı.');
    } catch (e) {
      _debugLog('SignalR bağlantısı kapatılırken hata: $e');
    }
  }
}

void _debugLog(Object? message) {
  if (kDebugMode) debugPrint(message.toString());
}
