import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import '../../../providers/auth_provider.dart';

class EmailVerificationPage extends StatefulWidget {
  const EmailVerificationPage({super.key, required this.email});
  final String email;
  @override
  State<EmailVerificationPage> createState() => _EmailVerificationPageState();
}

class _EmailVerificationPageState extends State<EmailVerificationPage> {
  final _code = TextEditingController();
  bool _busy = false;
  String? _message;
  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  Future<void> _submit({required bool resend}) async {
    if (_busy) return;
    if (!resend && !RegExp(r'^\d{6}$').hasMatch(_code.text)) {
      setState(() => _message = '6 haneli kodu gir.');
      return;
    }
    setState(() { _busy = true; _message = null; });
    try {
      final auth = context.read<AuthProvider>();
      final result = resend
          ? await auth.resendVerification(email: widget.email)
          : await auth.verifyEmail(email: widget.email, code: _code.text);
      if (!mounted) return;
      if (result.success && !resend) {
        Navigator.pop(context, true);
        return;
      }
      setState(() => _message = result.success
          ? 'Kod gönderme isteği alındı. Gelen kutunu kontrol et.'
          : result.message ?? 'İşlem başarısız. Tekrar dene.');
    } catch (_) {
      if (mounted) setState(() => _message = 'Bağlantı hatası. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('E-posta doğrulama')),
    body: SafeArea(child: Center(child: SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        const Icon(Icons.mark_email_read_outlined, size: 72, color: Color(0xFFD6B46A)),
        const SizedBox(height: 24),
        Text('${widget.email} adresine gönderilen kodu gir.', textAlign: TextAlign.center),
        const SizedBox(height: 24),
        TextField(controller: _code, enabled: !_busy, keyboardType: TextInputType.number,
            autofillHints: const [AutofillHints.oneTimeCode],
            inputFormatters: [FilteringTextInputFormatter.digitsOnly], maxLength: 6,
            decoration: const InputDecoration(labelText: 'Doğrulama kodu', border: OutlineInputBorder())),
        if (_message != null) Padding(padding: const EdgeInsets.only(bottom: 12), child: Text(_message!)),
        FilledButton(onPressed: _busy ? null : () => _submit(resend: false), child: const Text('Doğrula')),
        TextButton(onPressed: _busy ? null : () => _submit(resend: true), child: const Text('Tekrar kod gönder')),
        if (_busy) const Center(child: CircularProgressIndicator()),
      ]),
    ))),
  );
}
