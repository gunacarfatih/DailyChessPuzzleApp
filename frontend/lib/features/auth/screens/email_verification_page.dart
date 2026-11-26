
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';

import '../../../core/storage/auth_storage.dart';
import '../../../providers/auth_provider.dart';

class EmailVerificationPage extends StatefulWidget {
  const EmailVerificationPage({
    super.key,
    required this.email,
  });

  final String email;

  @override
  State<EmailVerificationPage> createState() =>
      _EmailVerificationPageState();
}

class _EmailVerificationPageState
    extends State<EmailVerificationPage> {
  static const Color _primary = Color(0xFFB7D7A8);
  static const Color _secondary = Color(0xFFD6B46A);
  static const Color _surface = Color(0xFF1B2923);

  final TextEditingController _code =
  TextEditingController();

  final AuthStorage _authStorage = AuthStorage.instance;

  late final int _sessionGeneration;

  bool _busy = false;
  bool _isError = false;
  String? _message;

  @override
  void initState() {
    super.initState();

    _sessionGeneration = _authStorage.sessionGeneration;

    // Ekran açıldıktan sonra doğrulama kodu iste.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      _submit(resend: true);
    });
  }

  bool _isCurrentSession(AuthProvider auth) {
    return _authStorage.isCurrentGeneration(
      _sessionGeneration,
    ) &&
        auth.isLoggedIn &&
        auth.email?.trim().toLowerCase() ==
            widget.email.trim().toLowerCase();
  }

  void _showMessage(
      String message, {
        bool isError = false,
      }) {
    if (!mounted) return;

    setState(() {
      _message = message;
      _isError = isError;
    });
  }

  Future<void> _submit({
    required bool resend,
  }) async {
    if (!mounted || _busy) return;

    final auth = context.read<AuthProvider>();

    if (!_isCurrentSession(auth)) {
      _showMessage(
        'Oturum değişti. Lütfen profil ekranına dön.',
        isError: true,
      );
      return;
    }

    if (auth.emailVerified) {
      Navigator.pop(context, true);
      return;
    }

    final code = _code.text.trim();

    if (!resend && !RegExp(r'^\d{6}$').hasMatch(code)) {
      _showMessage(
        '6 haneli doğrulama kodunu gir.',
        isError: true,
      );
      return;
    }

    setState(() {
      _busy = true;
      _message = null;
      _isError = false;
    });

    try {
      final result = resend
          ? await auth.resendVerification(
        email: widget.email,
      )
          : await auth.verifyEmail(
        email: widget.email,
        code: code,
      );

      if (!mounted) return;

      if (!_isCurrentSession(auth)) {
        _showMessage(
          'Oturum değişti. Lütfen profil ekranına dön.',
          isError: true,
        );
        return;
      }

      if (result.success && !resend) {
        Navigator.pop(context, true);
        return;
      }

      if (result.success) {
        _code.clear();

        _showMessage(
          'Kod gönderme isteği alındı. '
              'Gelen kutunu ve spam klasörünü kontrol et.',
        );
      } else {
        _showMessage(
          result.message ??
              'İşlem başarısız. Lütfen tekrar dene.',
          isError: true,
        );
      }
    } catch (_) {
      if (!mounted) return;

      if (!_isCurrentSession(auth)) {
        _showMessage(
          'Oturum değişti. Lütfen profil ekranına dön.',
          isError: true,
        );
      } else {
        _showMessage(
          'İşlem tamamlanamadı. Lütfen tekrar dene.',
          isError: true,
        );
      }
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
        });
      }
    }
  }

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final isCurrentSession = _isCurrentSession(auth);

    final isDisabled =
        _busy || !isCurrentSession || auth.emailVerified;

    return Scaffold(
      appBar: AppBar(
        title: const Text('E-posta Doğrulama'),
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Container(
              padding: const EdgeInsets.all(24),
              decoration: BoxDecoration(
                color: _surface,
                borderRadius: BorderRadius.circular(22),
                border: Border.all(
                  color: Colors.white10,
                ),
              ),
              child: Column(
                crossAxisAlignment:
                CrossAxisAlignment.stretch,
                children: [
                  const Icon(
                    Icons.mark_email_read_outlined,
                    size: 72,
                    color: _secondary,
                  ),

                  const SizedBox(height: 24),

                  const Text(
                    'E-posta adresini doğrula',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 21,
                      fontWeight: FontWeight.bold,
                    ),
                  ),

                  const SizedBox(height: 12),

                  Text(
                    widget.email,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: _primary,
                      fontSize: 15,
                      fontWeight: FontWeight.w600,
                    ),
                  ),

                  const SizedBox(height: 12),

                  const Text(
                    'E-posta adresine gönderilen '
                        '6 haneli doğrulama kodunu gir.',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Colors.white70,
                      fontSize: 13,
                      height: 1.5,
                    ),
                  ),

                  const SizedBox(height: 8),

                  const Text(
                    'Doğrulama kodu 15 dakika geçerlidir.',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Colors.white54,
                      fontSize: 12,
                    ),
                  ),

                  const SizedBox(height: 28),

                  TextField(
                    controller: _code,
                    enabled: !isDisabled,
                    keyboardType: TextInputType.number,
                    textInputAction: TextInputAction.done,
                    textAlign: TextAlign.center,
                    autofillHints: const [
                      AutofillHints.oneTimeCode,
                    ],
                    inputFormatters: [
                      FilteringTextInputFormatter.digitsOnly,
                    ],
                    maxLength: 6,
                    style: const TextStyle(
                      fontSize: 24,
                      letterSpacing: 8,
                      fontWeight: FontWeight.bold,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'Doğrulama kodu',
                      hintText: '000000',
                      counterText: '',
                      border: OutlineInputBorder(),
                    ),
                    onSubmitted: isDisabled
                        ? null
                        : (_) => _submit(resend: false),
                  ),

                  if (!isCurrentSession) ...[
                    const SizedBox(height: 16),
                    const Text(
                      'Oturum değişti. Lütfen profil ekranına dön.',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        color: Colors.redAccent,
                        fontSize: 13,
                      ),
                    ),
                  ],

                  if (_message != null) ...[
                    const SizedBox(height: 16),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: (_isError
                            ? Colors.redAccent
                            : _primary)
                            .withValues(alpha: 0.08),
                        borderRadius:
                        BorderRadius.circular(12),
                      ),
                      child: Text(
                        _message!,
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          color: _isError
                              ? Colors.redAccent
                              : _primary,
                          fontSize: 13,
                          height: 1.5,
                        ),
                      ),
                    ),
                  ],

                  const SizedBox(height: 24),

                  SizedBox(
                    height: 48,
                    child: FilledButton.icon(
                      onPressed: isDisabled
                          ? null
                          : () => _submit(resend: false),
                      icon: const Icon(Icons.verified_outlined),
                      label: const Text('E-postayı Doğrula'),
                      style: FilledButton.styleFrom(
                        backgroundColor: _primary,
                        foregroundColor:
                        const Color(0xFF101A17),
                      ),
                    ),
                  ),

                  const SizedBox(height: 12),

                  TextButton.icon(
                    onPressed: isDisabled
                        ? null
                        : () => _submit(resend: true),
                    icon: const Icon(Icons.refresh),
                    label: const Text('Tekrar Kod Gönder'),
                  ),

                  if (_busy) ...[
                    const SizedBox(height: 12),
                    const Center(
                      child: CircularProgressIndicator(
                        color: _primary,
                      ),
                    ),
                  ],

                  const SizedBox(height: 12),

                  TextButton(
                    onPressed: _busy
                        ? null
                        : () => Navigator.pop(context),
                    child: const Text('Daha Sonra'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
