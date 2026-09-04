import mongoose from 'mongoose';
import bcrypt from 'bcryptjs';
import SecurityUtils from '../utils/SecurityUtils.js';

/**
 * MongoDB schema for clients/organizations
 * Each client represents a business/organization using the monitoring service
 */
const clientSchema = new mongoose.Schema(
    {
        name: {
            type: String,
            required: true,
            trim: true,
            minlength: 2,
            maxlength: 100,
        },
        slug: {
            type: String,
            required: true,
            unique: true,
            trim: true,
            lowercase: true,
            match: /^[a-z0-9-]+$/,
        },
        email: {
            type: String,
            required: true,
            lowercase: true,
            trim: true,
        },
        password: {
            type: String,
            required: true,
            minlength: 6,
            validate: {
                validator: function (password) {
                    if (this.isModified('password') && password && !password.startsWith('$2a$')) {
                        const validation = SecurityUtils.validatePassword(password)
                        return validation.success
                    };
                    return true
                },
                message: function (props) {
                    if (props.value && !props.value.startsWith('$2a$')) {
                        const validation = SecurityUtils.validatePassword(props.value)
                        // ["Password is required", "Password must contain at least one uppercase letter"]
                        // "Password is required. Password must contain at least one uppercase letter."
                        return validation.errors.join(". ");
                    };
                    return "Password validation failed"
                }
            },
        },
        description: {
            type: String,
            maxlength: 500,
            default: '',
        },
        website: {
            type: String,
            default: '',
        },
        isActive: {
            type: Boolean,
            default: true,
        },
        settings: {
            dataRetentionDays: {
                type: Number,
                default: 30,
                min: 7,
                max: 365,
            },
            alertsEnabled: {
                type: Boolean,
                default: true,
            },
            timezone: {
                type: String,
                default: 'UTC',
            },
        },
    },
    {
        timestamps: true,
        collection: 'clients',
    }
);

clientSchema.pre('save', async function () {
    if (!this.isModified('password')) {
        return;
    }

    const salt = await bcrypt.genSalt(10);
    this.password = await bcrypt.hash(this.password, salt);
});

// Compare password method
clientSchema.methods.comparePassword = async function (candidatePassword) {
    return bcrypt.compare(candidatePassword, this.password);
};

clientSchema.index({ isActive: 1 });

const Client = mongoose.model('Client', clientSchema);

export default Client;